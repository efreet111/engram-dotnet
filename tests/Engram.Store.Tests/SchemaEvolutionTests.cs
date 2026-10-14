using Engram.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// ENG-416: Schema evolution — ledger, code metadata columns, indexes, guard.
/// SQLite backend (T2).
/// </summary>
public class SchemaEvolutionTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;
    private const string SessionId = "schema-evo-test-session";

    public SchemaEvolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SqliteConnection OpenRaw()
    {
        var dbPath = Path.Combine(_tempDir, "engram.db");
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private Task SeedSession()
        => _store.CreateSessionAsync(SessionId, "test-project", "/tmp");

    // ─── FR-001: Ledger table exists ────────────────────────────────────────

    [Fact]
    public void FreshInstall_SchemaMigrationsLedgerExists()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        var count = System.Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public void FreshInstall_LedgerContainsBaselineAndV1()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version";
        using var r = cmd.ExecuteReader();

        var rows = new List<(long Version, string Name)>();
        while (r.Read())
            rows.Add((r.GetInt64(0), r.GetString(1)));

        Assert.Equal(2, rows.Count);
        Assert.Equal((0L, "baseline_pre_eng416"), rows[0]);
        Assert.Equal((1L, "eng416_code_metadata"), rows[1]);
    }

    // ─── FR-001: Columns exist ──────────────────────────────────────────────

    [Fact]
    public void FreshInstall_ObservationsHasMetadataColumns()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(observations)";
        using var r = cmd.ExecuteReader();

        var columns = new List<string>();
        while (r.Read())
            columns.Add(r.GetString(1)); // col 1 = name

        Assert.Contains("file_path", columns);
        Assert.Contains("symbol", columns);
        Assert.Contains("namespace", columns);
    }

    // ─── FR-002: Indexes exist ──────────────────────────────────────────────

    [Fact]
    public void FreshInstall_MetadataIndexesExist()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='index' AND name IN ('idx_obs_file_path', 'idx_obs_symbol', 'idx_obs_namespace')";
        var count = System.Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    // ─── FR-003: Idempotency ────────────────────────────────────────────────

    [Fact]
    public void DoubleStart_NoDuplicateLedgerRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = new StoreConfig { DataDir = dir };
            using (var s1 = new SqliteStore(cfg)) { }
            using (var s2 = new SqliteStore(cfg)) { }

            using var conn = new SqliteConnection($"Data Source={Path.Combine(dir, "engram.db")}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations";
            var count = System.Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(2, count);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ─── FR-004: Roundtrip ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveWithMetadata_RoundtripsExactValues()
    {
        await SeedSession();
        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Test",
            Content = "Content",
            Type = "decision",
            Project = "test-project",
            FilePath = "src/Auth/JwtBearer.cs",
            Symbol = "JwtBearerHandler",
            Namespace = "Engram.Auth",
        });

        var obs = await _store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("src/Auth/JwtBearer.cs", obs.FilePath);
        Assert.Equal("JwtBearerHandler", obs.Symbol);
        Assert.Equal("Engram.Auth", obs.Namespace);
    }

    [Fact]
    public async Task SaveWithoutMetadata_NullFields()
    {
        await SeedSession();
        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Test",
            Content = "Content",
            Type = "decision",
            Project = "test-project",
        });

        var obs = await _store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Null(obs.FilePath);
        Assert.Null(obs.Symbol);
        Assert.Null(obs.Namespace);
    }

    // ─── FR-005: Guard ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveWithMetadataOver512Chars_ThrowsDescriptiveError()
    {
        await SeedSession();
        var longPath = new string('a', 600);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.AddObservationAsync(new AddObservationParams
            {
                SessionId = SessionId,
                Title = "Test",
                Content = "Content",
                Type = "decision",
                FilePath = longPath,
            }));

        Assert.Contains("FilePath", ex.Message);
        Assert.Contains("512", ex.Message);
    }

    [Fact]
    public async Task SaveWithExactly512CharsMultibyte_Succeeds()
    {
        await SeedSession();
        // 512 chars, each 2 bytes in UTF-8 (e.g., 'ñ' = 2 bytes)
        var symbol = new string('ñ', 512);

        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Test",
            Content = "Content",
            Type = "decision",
            Symbol = symbol,
        });

        var obs = await _store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal(512, obs.Symbol!.Length);
    }

    // ─── Regression ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistingTests_StillPass()
    {
        // Smoke test: basic save/get still works
        await SeedSession();
        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Regression",
            Content = "Test content",
            Type = "manual",
            Project = "test-project",
        });
        var obs = await _store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("Regression", obs.Title);
    }
}
