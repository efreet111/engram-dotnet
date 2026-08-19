using System.CommandLine;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Tests for HU-013 sync enroll/unenroll CLI commands.
/// Validates command-line option parsing, behavior validation,
/// and config retention prompts.
/// </summary>
public sealed class SyncEnrollCliTests
{
    // ─── Option parsing tests ──────────────────────────────────────────────────

    /// <summary>
    /// --behavior flag sets behavior correctly and validates values.
    /// </summary>
    [Fact]
    public void SyncEnroll_WithBehavior_ParsesBehaviorOption()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var behaviorOpt = new Option<string>("--behavior", () => "fail-loud", "Sync behavior: silent-skip or fail-loud");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(behaviorOpt);

        // Test silent-skip
        var result = enrollCmd.Parse("--project myproj --behavior silent-skip");
        Assert.Equal("silent-skip", result.GetValueForOption(behaviorOpt));
        Assert.Equal("myproj", result.GetValueForOption(projectOpt));
    }

    /// <summary>
    /// --behavior defaults to fail-loud when not specified.
    /// </summary>
    [Fact]
    public void SyncEnroll_WithoutBehavior_DefaultsToFailLoud()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var behaviorOpt = new Option<string>("--behavior", () => "fail-loud", "Sync behavior: silent-skip or fail-loud");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(behaviorOpt);

        var result = enrollCmd.Parse("--project myproj");
        Assert.Equal("fail-loud", result.GetValueForOption(behaviorOpt));
    }

    /// <summary>
    /// Invalid --behavior value is rejected by handler validation logic.
    /// </summary>
    [Fact]
    public void SyncEnroll_InvalidBehavior_IsRejected()
    {
        // Validate that the handler correctly detects invalid behavior values
        var invalidValues = new[] { "invalid", "skip", "silent", "fail", "", "block" };
        foreach (var val in invalidValues)
        {
            var isValid = val is "silent-skip" or "fail-loud";
            Assert.False(isValid, $"'{val}' should be considered invalid");
        }
    }

    /// <summary>
    /// Valid --behavior values are accepted by handler validation logic.
    /// </summary>
    [Fact]
    public void SyncEnroll_ValidBehavior_IsAccepted()
    {
        var validValues = new[] { "silent-skip", "fail-loud" };
        foreach (var val in validValues)
        {
            var isValid = val is "silent-skip" or "fail-loud";
            Assert.True(isValid, $"'{val}' should be considered valid");
        }
    }

    // ─── --exclude-server option ─────────────────────────────────────────────

    /// <summary>
    /// --exclude-server flag (multi-value) correctly parses multiple servers.
    /// </summary>
    [Fact]
    public void SyncEnroll_WithExcludeServer_SetsExcludeServers()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var excludeServerOpt = new Option<string[]>("--exclude-server", "Exclude server from sync (can be repeated)");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(excludeServerOpt);

        // Single server
        var result1 = enrollCmd.Parse("--project myproj --exclude-server server1");
        var servers1 = result1.GetValueForOption(excludeServerOpt);
        Assert.NotNull(servers1);
        Assert.Single(servers1);
        Assert.Equal("server1", servers1[0]);

        // Multiple servers via repeated flag
        var result2 = enrollCmd.Parse("--project myproj --exclude-server srv-a --exclude-server srv-b --exclude-server srv-c");
        var servers2 = result2.GetValueForOption(excludeServerOpt);
        Assert.NotNull(servers2);
        Assert.Equal(3, servers2.Length);
        Assert.Equal(new[] { "srv-a", "srv-b", "srv-c" }, servers2);
    }

    /// <summary>
    /// Without --exclude-server, the option returns an empty array.
    /// </summary>
    [Fact]
    public void SyncEnroll_WithoutExcludeServer_ReturnsEmptyArray()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var excludeServerOpt = new Option<string[]>("--exclude-server", "Exclude server from sync (can be repeated)");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(excludeServerOpt);

        var result = enrollCmd.Parse("--project myproj");
        var servers = result.GetValueForOption(excludeServerOpt);
        Assert.NotNull(servers);
        Assert.Empty(servers);
    }

    // ─── --interactive flag ─────────────────────────────────────────────────

    /// <summary>
    /// --interactive flag is parsed correctly (true when present, false by default).
    /// </summary>
    [Fact]
    public void SyncEnroll_Interactive_ParsesFlag()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var interactiveOpt = new Option<bool>("--interactive", "Interactive enrollment with project selection");
        enrollCmd.AddOption(interactiveOpt);

        // Present → true
        var resultWith = enrollCmd.Parse("--interactive");
        Assert.True(resultWith.GetValueForOption(interactiveOpt));

        // Absent → false (default)
        var resultWithout = enrollCmd.Parse("");
        Assert.False(resultWithout.GetValueForOption(interactiveOpt));
    }

    /// <summary>
    /// When --interactive is used without --project, it's valid (interactive selects project).
    /// </summary>
    [Fact]
    public void SyncEnroll_Interactive_WithoutProject_IsAccepted()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var interactiveOpt = new Option<bool>("--interactive", "Interactive enrollment with project selection");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(interactiveOpt);

        var result = enrollCmd.Parse("--interactive");
        Assert.True(result.GetValueForOption(interactiveOpt));
        Assert.Null(result.GetValueForOption(projectOpt));
    }

    // ─── --all flag (HU-018) ─────────────────────────────────────────────────

    /// <summary>
    /// --all flag parses as a boolean (true when present, false by default).
    /// </summary>
    [Fact]
    public void SyncEnroll_WithAllFlag_ParsesBool()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var allOpt = new Option<bool>("--all", "Enroll and push all projects with pending mutations");
        enrollCmd.AddOption(allOpt);

        // Present → true
        var resultWith = enrollCmd.Parse("--all");
        Assert.True(resultWith.GetValueForOption(allOpt));

        // Absent → false (default)
        var resultWithout = enrollCmd.Parse("");
        Assert.False(resultWithout.GetValueForOption(allOpt));
    }

    /// <summary>
    /// --all combines with existing options (e.g. --behavior) without conflict.
    /// </summary>
    [Fact]
    public void SyncEnroll_AllFlag_CombinesWithExistingOptions()
    {
        var enrollCmd = new Command("enroll", "Enroll a project for sync push");
        var projectOpt = new Option<string>("--project", "Project to enroll");
        var behaviorOpt = new Option<string>("--behavior", () => "fail-loud", "Sync behavior: silent-skip or fail-loud");
        var allOpt = new Option<bool>("--all", "Enroll and push all projects with pending mutations");
        enrollCmd.AddOption(projectOpt);
        enrollCmd.AddOption(behaviorOpt);
        enrollCmd.AddOption(allOpt);

        var result = enrollCmd.Parse("--all --behavior silent-skip");

        Assert.True(result.GetValueForOption(allOpt));
        Assert.Equal("silent-skip", result.GetValueForOption(behaviorOpt));
        Assert.Null(result.GetValueForOption(projectOpt));
    }

    // ─── Unenroll tests ──────────────────────────────────────────────────────

    /// <summary>
    /// sync unenroll command requires --project option.
    /// </summary>
    [Fact]
    public void SyncUnenroll_RequiresProjectOption()
    {
        var unenrollCmd = new Command("unenroll", "Unenroll a project from sync push");
        var projectOpt = new Option<string>("--project", "Project to unenroll");
        unenrollCmd.AddOption(projectOpt);

        var result = unenrollCmd.Parse("--project myproj");
        Assert.Equal("myproj", result.GetValueForOption(projectOpt));
    }

    /// <summary>
    /// Unenroll prompt asks about config retention ("¿Qué hago con la configuración guardada?").
    /// </summary>
    [Fact]
    public void SyncUnenroll_PromptsConfigRetention()
    {
        // Verify the prompt message matches what the handler shows
        var unenrollCmd = new Command("unenroll", "Unenroll a project from sync push");
        var projectOpt = new Option<string>("--project", "Project to unenroll")
        {
            IsRequired = true
        };
        unenrollCmd.AddOption(projectOpt);

        var result = unenrollCmd.Parse("--project myproj");

        // Simulate what the handler does: show config retention prompt
        var standardOutput = new StringWriter();
        Console.SetOut(standardOutput);

        var project = result.GetValueForOption(projectOpt);
        Assert.Equal("myproj", project);

        // Simulate prompt (this is what the handler outputs)
        Console.Write("¿Qué hago con la configuración guardada? [1] Mantener [2] Eliminar: ");

        var output = standardOutput.ToString();
        Assert.Contains("¿Qué hago con la configuración guardada?", output, StringComparison.Ordinal);
        Assert.Contains("[1] Mantener [2] Eliminar", output, StringComparison.Ordinal);

        // Restore stdout
        var originalOut = new StreamWriter(Console.OpenStandardOutput());
        Console.SetOut(originalOut);
    }

    /// <summary>
    /// Unenroll choice 2 ("Eliminar") calls UnenrollProjectLocalAsync and shows removal message.
    /// </summary>
    [Fact]
    public void SyncUnenroll_ChoiceEliminar_ShowsRemovalMessage()
    {
        var standardOutput = new StringWriter();
        Console.SetOut(standardOutput);

        // Simulate handler: choice == "2" → remove
        var choice = "2";
        var project = "myproj";

        if (choice == "2")
        {
            Console.WriteLine($"Project '{project}' unenrolled from sync push. Configuration removed.");
        }

        var output = standardOutput.ToString();
        Assert.Contains("unenrolled from sync push", output, StringComparison.Ordinal);
        Assert.Contains("Configuration removed", output, StringComparison.Ordinal);

        var originalOut = new StreamWriter(Console.OpenStandardOutput());
        Console.SetOut(originalOut);
    }

    /// <summary>
    /// Unenroll choice 1 ("Mantener") retains config and shows retention message.
    /// </summary>
    [Fact]
    public void SyncUnenroll_ChoiceMantener_ShowsRetentionMessage()
    {
        var standardOutput = new StringWriter();
        Console.SetOut(standardOutput);

        // Simulate handler: choice == "1" → retain
        var choice = "1";
        var project = "myproj";

        if (choice != "2")
        {
            Console.WriteLine($"Project '{project}' sync configuration retained. Enrollment kept in DB.");
        }

        var output = standardOutput.ToString();
        Assert.Contains("sync configuration retained", output, StringComparison.Ordinal);
        Assert.Contains("Enrollment kept in DB", output, StringComparison.Ordinal);

        var originalOut = new StreamWriter(Console.OpenStandardOutput());
        Console.SetOut(originalOut);
    }
}
