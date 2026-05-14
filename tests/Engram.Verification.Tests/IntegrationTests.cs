using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

public class IntegrationTests : IDisposable
{
    private readonly IStore _store;
    private readonly string _testDir;
    private readonly CycleTracker _cycleTracker;

    public IntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-verify-int-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
        _cycleTracker = new CycleTracker(_store);
    }

    [Fact]
    public async Task SpecParser_To_CycleTracker_FullFlow()
    {
        // Create a spec file
        var specPath = Path.Combine(_testDir, "spec.md");
        await File.WriteAllTextAsync(specPath, @"
## Objective
Test full flow

## Functional Requirements
- RF-001: Do the thing
- RF-002: Do another thing
");

        // Parse
        var parser = new SpecParser();
        var markdown = await File.ReadAllTextAsync(specPath);
        var spec = parser.Parse(markdown);

        Assert.False(spec.IsUnparseable);
        Assert.Equal(2, spec.Requirements.Count);

        // Track cycle
        await _store.CreateSessionAsync("int-test-session", "engram", "");
        var cycle = await _cycleTracker.IncrementCycleAsync("integration-test", "int-test-session");
        Assert.Equal(1, cycle);

        // Build traceability matrix (no observations yet -> all missing)
        var builder = new TraceabilityMatrixBuilder(_store);
        var matrix = await builder.BuildMatrixAsync(spec, "engram");
        Assert.Equal(2, matrix.Total);
        Assert.Equal(0, matrix.Covered);
        Assert.Equal(2, matrix.Missing);
    }

    [Fact]
    public async Task FakeVerifier_ReturnsConfiguredResult()
    {
        var expectedReport = new VerificationReport
        {
            Total = 1,
            Passed = 1,
            Summary = "All good",
            Items = new List<VerificationItem>
            {
                new()
                {
                    Requirement = new Requirement { Id = "RF-001", Description = "Test" },
                    Verdict = Verdict.Pass,
                    Confidence = 1.0
                }
            }
        };

        var verifier = new FakeVerifier { Result = expectedReport };
        var spec = new SpecParseResult { Objective = "Test" };

        var result = await verifier.VerifyAsync(spec, "", 1);

        Assert.Equal(1, result.Passed);
        Assert.Equal(Verdict.Pass, result.Items[0].Verdict);
        Assert.Equal(1, result.Cycle);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
