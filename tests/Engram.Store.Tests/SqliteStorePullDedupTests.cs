using Engram.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// ENG-457: Sync pull was inserting the same mutation millions of times when
/// the pull cursor regressed (e.g. on restart or first-ever sync against a
/// server with historical data). Fix:
///   1. UNIQUE PARTIAL INDEX on (target_key, entity_key) WHERE source='pull'
///   2. INSERT OR IGNORE in InsertPulledMutationAsync
///
/// These tests verify both.
/// </summary>
public sealed class SqliteStorePullDedupTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;

    public SqliteStorePullDedupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-dedup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static SyncMutation MakeMutation(string entityKey, string op = "upsert")
        => new(0, "cloud", "observation", entityKey, op,
               """{"sync_id":"x","session_id":"s","type":"manual","title":"t","content":"c","project":"p","scope":"team"}""",
               "pull", "p", DateTime.UtcNow, null);

    [Fact]
    public async Task InsertPulledMutationAsync_FirstInsert_ReturnsSeq()
    {
        var seq = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-A"));
        Assert.True(seq > 0);
    }

    [Fact]
    public async Task InsertPulledMutationAsync_DuplicateEntityKey_ReturnsSameSeq()
    {
        // First pull: server sends mutation for "obs-A", client inserts.
        var firstSeq = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-A"));

        // Cursor reset or repeated cycle: same mutation arrives again.
        // The fix returns the EXISTING seq instead of inserting a duplicate row.
        var secondSeq = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-A", op: "delete"));

        Assert.Equal(firstSeq, secondSeq);
    }

    [Fact]
    public async Task InsertPulledMutationAsync_DuplicateDoesNotInsertNewRow()
    {
        await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-B"));
        await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-B"));
        await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-B"));

        // Count rows in sync_mutations for this entity_key — must be exactly 1.
        using var conn = new SqliteConnection($"Data Source={_tempDir}/engram.db");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_mutations WHERE target_key='cloud' AND entity_key='obs-B' AND source='pull'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InsertPulledMutationAsync_DifferentEntityKeys_InsertsSeparate()
    {
        // Dedup is per-entity_key, not a global uniqueness.
        var seqA = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-A"));
        var seqB = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-B"));

        Assert.NotEqual(seqA, seqB);
    }

    [Fact]
    public async Task InsertPulledMutationAsync_PullSource_DedupedByEntityKey()
    {
        // Verified via the partial index in SyncMutationsTable_HasUniquePullIndex.
        // Multiple inserts with same entity_key return the same seq.
        var firstSeq = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-X"));
        var secondSeq = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-X"));
        Assert.Equal(firstSeq, secondSeq);
    }

    [Fact]
    public async Task InsertPulledMutationAsync_DifferentTargetKey_SameEntityKey_BothInsert()
    {
        // Two different sync targets (e.g. multiple relays) can have the same
        // entity_key — the UNIQUE INDEX is per-target.
        // Initialize sync_state for both targets via a manual insert
        // (UpdateSyncStateAsync only UPDATEs an existing row).
        using (var conn = new SqliteConnection($"Data Source={_tempDir}/engram.db"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sync_state (target_key, lifecycle, last_pulled_seq) VALUES ('other', 'idle', 0)";
            cmd.ExecuteNonQuery();
        }

        var seqCloud = await _store.InsertPulledMutationAsync("cloud", MakeMutation("obs-Y"));
        var seqOther = await _store.InsertPulledMutationAsync("other", MakeMutation("obs-Y"));

        Assert.NotEqual(seqCloud, seqOther);
    }

    [Fact]
    public void SyncMutationsTable_HasUniquePullIndex()
    {
        // Schema test: confirm the partial UNIQUE INDEX exists after init.
        using var conn = new SqliteConnection($"Data Source={_tempDir}/engram.db");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT name, sql FROM sqlite_master
            WHERE type='index' AND name='idx_sync_mutations_pull_dedup'";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read(), "idx_sync_mutations_pull_dedup index should exist");
        var sql = r.GetString(1);
        Assert.Contains("UNIQUE", sql);
        Assert.Contains("source = 'pull'", sql);
    }
}