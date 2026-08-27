using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Collection for tests that mutate process environment variables (ENGRAM_PROFILE,
/// ENGRAM_CONFIG_DIR, etc.). Tests in this collection run serially to avoid
/// cross-test env leaks, since <c>Environment.SetEnvironmentVariable</c> is process-global.
/// </summary>
[CollectionDefinition("EnvSensitive", DisableParallelization = true)]
public sealed class ProfileEnvCollection : ICollectionFixture<object>;

/// <summary>
/// Scoped environment-variable mutator. Records the original value of each key the
/// first time it is touched and restores them all on dispose, so tests never leak env state.
/// </summary>
internal sealed class EnvVarScope : IDisposable
{
    private readonly List<(string Key, string? Original)> _saved = [];

    public EnvVarScope Set(string key, string? value)
    {
        _saved.Add((key, Environment.GetEnvironmentVariable(key)));
        Environment.SetEnvironmentVariable(key, value);
        return this;
    }

    public void Dispose()
    {
        foreach (var (key, original) in _saved)
            Environment.SetEnvironmentVariable(key, original);
    }
}
