using System.Text.RegularExpressions;
using Xunit;

namespace Engram.Verification.Tests;

/// <summary>
/// ENG-479 contract tests for the Docker runtime permission handoff.
/// These tests keep the two Dockerfiles and their shared entrypoint in sync.
/// </summary>
public sealed class DockerRuntimePermissionsTests
{
    [Fact]
    public void Entrypoint_RepairsDataOwnershipAndDropsPrivileges()
    {
        var entrypoint = ReadRepositoryFile("entrypoint.sh");

        Assert.StartsWith("#!/bin/bash", entrypoint, StringComparison.Ordinal);
        Assert.Contains("set -eEuo pipefail", entrypoint, StringComparison.Ordinal);
        Assert.Contains("if [ -d \"/data/engram\" ] && [ ! -w \"/data/engram\" ]; then", entrypoint, StringComparison.Ordinal);
        Assert.Contains(
            "chown -R engram:engram /data/engram 2>&1",
            entrypoint,
            StringComparison.Ordinal);
        Assert.Contains(
            "echo \"[entrypoint] Warning: chown failed",
            entrypoint,
            StringComparison.Ordinal);
        Assert.Contains("exec gosu engram \"$@\"", entrypoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Entrypoint_IsExecutableOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(RepositoryPath("entrypoint.sh"));

        Assert.True(
            mode.HasFlag(UnixFileMode.UserExecute),
            "entrypoint.sh must have the user-execute bit set.");
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData("Dockerfile.debian")]
    public void Dockerfile_UsesSharedRootEntrypoint(string dockerfileName)
    {
        var dockerfile = ReadRepositoryFile(dockerfileName);

        Assert.Contains(
            "apt-get install -y --no-install-recommends gosu",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains("gosu nobody true", dockerfile, StringComparison.Ordinal);
        Assert.Contains(
            "COPY entrypoint.sh /usr/local/bin/entrypoint.sh",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains(
            "RUN chmod +x /usr/local/bin/entrypoint.sh",
            dockerfile,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"(?m)^\s*USER\s+engram\s*$"),
            dockerfile);
        Assert.Contains(
            "ENTRYPOINT [\"/usr/local/bin/entrypoint.sh\"]",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains("CMD [\"./engram\", \"serve\"]", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ENTRYPOINT [\"./engram\"]", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void DebianDockerfile_UsesMatchingAspNetRuntimeVersion()
    {
        var dockerfile = ReadRepositoryFile("Dockerfile.debian");

        Assert.Contains("ARG DOTNET_VERSION=10.0.108", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ARG DOTNET_RUNTIME_VERSION=10.0.8", dockerfile, StringComparison.Ordinal);
        Assert.Contains(
            "--version ${DOTNET_RUNTIME_VERSION} --runtime aspnetcore",
            dockerfile,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "--version ${DOTNET_VERSION} --runtime aspnetcore",
            dockerfile,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DockerCompose_StillMountsTheApplicationDataDirectory()
    {
        var compose = ReadRepositoryFile("docker/docker-compose.yml");
        var testCompose = ReadRepositoryFile("docker/docker-compose.test.yml");

        Assert.Contains("/data/engram", compose, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_DATA_DIR: /data/engram", compose, StringComparison.Ordinal);
        Assert.Contains("command: [\"./engram\", \"serve\"]", testCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("command: [\"serve\"]", testCompose, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerVanillaGuide_DocumentsPermissionsVariablesAndExamples()
    {
        var guide = ReadRepositoryFile("docs/DOCKER-VANILLA.md");

        Assert.Contains("## 8. Volume permissions", guide, StringComparison.Ordinal);
        Assert.Contains("## 9. Environment variables reference", guide, StringComparison.Ordinal);
        Assert.Contains("chown -R engram:engram /data/engram", guide, StringComparison.Ordinal);
        Assert.Contains("exec gosu engram \"$@\"", guide, StringComparison.Ordinal);
        Assert.Contains("docker run -d --name engram", guide, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_PG_CONNECTION", guide, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_SERVER_URL", guide, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_SYNC_ENABLED", guide, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_AUTO_ENROLL", guide, StringComparison.Ordinal);
        Assert.Contains("ENGRAM_PROJECT", guide, StringComparison.Ordinal);
        Assert.Contains("**Team mode (PostgreSQL, sync enabled)**", guide, StringComparison.Ordinal);
        Assert.Contains("**Custom port**", guide, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(RepositoryPath(relativePath));

    private static string RepositoryPath(string relativePath)
    {
        var directory = FindRepositoryRoot();
        return Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryRoot()
    {
        var starts = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory),
        };

        foreach (var start in starts)
        {
            for (var directory = start; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Dockerfile")) &&
                    File.Exists(Path.Combine(directory.FullName, "entrypoint.sh")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing Dockerfile and entrypoint.sh.");
    }
}
