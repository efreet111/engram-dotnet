using Engram.Store;
using Npgsql;
using Xunit;

namespace Engram.Postgres.Tests;

/// <summary>
/// ENG-416: Schema evolution — ledger, code metadata columns, indexes, guard.
/// PostgreSQL backend (T3/T4, RequiresDocker).
/// </summary>
[Trait("Category", "RequiresDocker")]
public class SchemaEvolutionPostgresTests : IClassFixture<PostgresStoreFixture>
{
    private readonly PostgresStoreFixture _fixture;
    private const string SessionId = "schema-evo-pg-test-session";

    public SchemaEvolutionPostgresTests(PostgresStoreFixture fixture)
    {
        _fixture = fixture;
    }

    private NpgsqlConnection OpenRaw()
    {
        var conn = new NpgsqlConnection(_fixture.ConnectionString);
        conn.Open();
        return conn;
    }

    private Task SeedSession()
        => _fixture.Store.CreateSessionAsync(SessionId, "test-project", "/tmp");

    // ─── FR-001: Ledger table exists ────────────────────────────────────────

    [Fact]
    public void FreshInstall_SchemaMigrationsLedgerExists()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'schema_migrations'";
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
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = 'observations' AND column_name IN ('file_path', 'symbol', 'namespace')";
        var count = System.Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    // ─── FR-002: Indexes exist ──────────────────────────────────────────────

    [Fact]
    public void FreshInstall_MetadataIndexesExist()
    {
        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM pg_indexes
            WHERE tablename = 'observations' AND indexname IN ('idx_obs_file_path', 'idx_obs_symbol', 'idx_obs_namespace')";
        var count = System.Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);
    }

    // ─── FR-003: Idempotency ────────────────────────────────────────────────

    [Fact]
    public void DoubleStart_NoDuplicateLedgerRows()
    {
        // Re-run Migrate() on the same DB via a second store instance.
        var cfg = new StoreConfig
        {
            DbType = StoreDbType.Postgres,
            PgConnectionString = _fixture.ConnectionString,
            DataDir = "/tmp",
        };
        using var second = new PostgresStore(cfg);

        using var conn = OpenRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations";
        var count = System.Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(2, count);
    }

    // ─── FR-004: Roundtrip ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveWithMetadata_RoundtripsExactValues()
    {
        await SeedSession();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var id = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = $"Test {unique}",
            Content = $"Content {unique}",
            Type = "decision",
            Project = "test-project",
            FilePath = "src/Auth/JwtBearer.cs",
            Symbol = "JwtBearerHandler",
            Namespace = "Engram.Auth",
        });

        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("src/Auth/JwtBearer.cs", obs.FilePath);
        Assert.Equal("JwtBearerHandler", obs.Symbol);
        Assert.Equal("Engram.Auth", obs.Namespace);
    }

    [Fact]
    public async Task SaveWithoutMetadata_NullFields()
    {
        await SeedSession();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var id = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = $"Test {unique}",
            Content = $"Content {unique}",
            Type = "decision",
            Project = "test-project",
        });

        var obs = await _fixture.Store.GetObservationAsync(id);
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
            _fixture.Store.AddObservationAsync(new AddObservationParams
            {
                SessionId = SessionId,
                Title = "Test",
                Content = $"Content {Guid.NewGuid():N}",
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

        var id = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Test",
            Content = $"Content {Guid.NewGuid():N}",
            Type = "decision",
            Symbol = symbol,
        });

        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal(512, obs.Symbol!.Length);
    }

    // ─── FR-002 / NFR-003: EXPLAIN index usage ──────────────────────────────

    [Fact]
    public async Task Explain_UsesIndexForMetadataQueries()
    {
        await SeedSession();
        var unique = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 5; i++)
        {
            await _fixture.Store.AddObservationAsync(new AddObservationParams
            {
                SessionId = SessionId,
                Title = $"Obs {unique} {i}",
                Content = $"Content {unique} {i}",
                Type = "decision",
                Project = "test-project",
                FilePath = $"src/Auth/File{i}.cs",
                Symbol = $"Symbol{i}",
                Namespace = "Engram.Auth",
            });
        }

        using var conn = OpenRaw();
        // Force index scan so the plan deterministically references the index.
        using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = "SET enable_seqscan = off";
            setCmd.ExecuteNonQuery();
        }
        using (var explainCmd = conn.CreateCommand())
        {
            explainCmd.CommandText = @"
                EXPLAIN SELECT * FROM observations
                WHERE file_path = 'src/Auth' OR file_path LIKE 'src/Auth/%'";
            var planBuilder = new System.Text.StringBuilder();
            using var r = explainCmd.ExecuteReader();
            while (r.Read())
                planBuilder.AppendLine(r.GetString(0));
            var plan = planBuilder.ToString();
            Assert.Contains("idx_obs_file_path", plan);
        }
    }
}
