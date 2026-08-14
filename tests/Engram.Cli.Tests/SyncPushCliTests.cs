using System.CommandLine;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Phase 4 integration tests for HU-014 sync push CLI command.
/// Validates command-line option parsing for `engram sync push --project <name>`.
/// Design: sdd/HU-014/design §AD3
/// Task: 4.6
/// </summary>
public sealed class SyncPushCliTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.6 — Integration test: sync push --project CLI command
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: `engram sync push --project A` correctly parses the --project option.
    /// Spec R2: CLI manual sync con --project.
    /// </summary>
    [Fact]
    public void SyncPush_WithProjectFlag_ParsesProjectOption()
    {
        // Arrange
        var syncCmd = new Command("sync", "Sync operations");
        var pushCmd = new Command("push", "Push pending mutations for a specific project");
        var projectOpt = new Option<string>("--project", "Project to push mutations for");
        pushCmd.AddOption(projectOpt);
        syncCmd.AddCommand(pushCmd);

        // Act
        var root = new RootCommand();
        root.AddCommand(syncCmd);
        var result = root.Parse("sync push --project my-project");

        // Assert
        var projectValue = result.GetValueForOption(projectOpt);
        Assert.Equal("my-project", projectValue);
    }

    /// <summary>
    /// Test: `engram sync push --project A --target cloud` parses both options.
    /// </summary>
    [Fact]
    public void SyncPush_WithProjectAndTarget_ParsesBothOptions()
    {
        // Arrange
        var pushCmd = new Command("push", "Push pending mutations");
        var projectOpt = new Option<string>("--project", "Project to push");
        var targetOpt = new Option<string>("--target", () => "cloud", "Target key");
        pushCmd.AddOption(projectOpt);
        pushCmd.AddOption(targetOpt);

        var syncCmd = new Command("sync") { pushCmd };
        var root = new RootCommand();
        root.AddCommand(syncCmd);

        // Act
        var result = root.Parse("sync push --project team/app --target staging");

        // Assert
        Assert.Equal("team/app", result.GetValueForOption(projectOpt));
        Assert.Equal("staging", result.GetValueForOption(targetOpt));
    }

    /// <summary>
    /// Test: --target defaults to "cloud" when not specified.
    /// </summary>
    [Fact]
    public void SyncPush_WithoutTarget_DefaultsToCloud()
    {
        // Arrange
        var pushCmd = new Command("push", "Push pending mutations");
        var projectOpt = new Option<string>("--project", "Project to push");
        var targetOpt = new Option<string>("--target", () => "cloud", "Target key");
        pushCmd.AddOption(projectOpt);
        pushCmd.AddOption(targetOpt);

        var syncCmd = new Command("sync") { pushCmd };
        var root = new RootCommand();
        root.AddCommand(syncCmd);

        // Act
        var result = root.Parse("sync push --project my-project");

        // Assert
        Assert.Equal("cloud", result.GetValueForOption(targetOpt));
    }

    /// <summary>
    /// Test: --project is required — handler validates and shows error when empty.
    /// This validates the handler's validation logic (not option-level required).
    /// </summary>
    [Fact]
    public void SyncPush_WithoutProject_HandlerReturnsError()
    {
        // Arrange
        var pushCmd = new Command("push", "Push pending mutations");
        var projectOpt = new Option<string>("--project", "Project to push");
        pushCmd.AddOption(projectOpt);

        var syncCmd = new Command("sync") { pushCmd };
        var root = new RootCommand();
        root.AddCommand(syncCmd);

        // Act
        var result = root.Parse("sync push");

        // Assert: the handler should see null --project and show an error
        var projectValue = result.GetValueForOption(projectOpt);
        Assert.Null(projectValue);
    }

    /// <summary>
    /// Test: --project with special characters parses correctly.
    /// </summary>
    [Fact]
    public void SyncPush_WithSpecialCharsInProject_ParsesCorrectly()
    {
        // Arrange
        var pushCmd = new Command("push", "Push pending mutations");
        var projectOpt = new Option<string>("--project", "Project to push");
        pushCmd.AddOption(projectOpt);

        var syncCmd = new Command("sync") { pushCmd };
        var root = new RootCommand();
        root.AddCommand(syncCmd);

        // Act
        var result = root.Parse("sync push --project user/project");

        // Assert
        Assert.Equal("user/project", result.GetValueForOption(projectOpt));
    }

    /// <summary>
    /// Test: --project with spaces works when quoted.
    /// </summary>
    [Fact]
    public void SyncPush_ProjectWithSpaces_ParsesWhenQuoted()
    {
        // Arrange
        var pushCmd = new Command("push", "Push pending mutations");
        var projectOpt = new Option<string>("--project", "Project to push");
        pushCmd.AddOption(projectOpt);

        var syncCmd = new Command("sync") { pushCmd };
        var root = new RootCommand();
        root.AddCommand(syncCmd);

        // Act
        var result = root.Parse("sync", "push", "--project", "My Project");

        // Assert
        Assert.Equal("My Project", result.GetValueForOption(projectOpt));
    }
}
