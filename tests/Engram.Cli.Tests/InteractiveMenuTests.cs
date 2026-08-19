using System;
using System.IO;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-018: Unit tests for the zero-dependency interactive TUI navigator.
/// Uses StringReader/StringWriter injection (design AD5) to drive menu navigation
/// without a real terminal. Navigation-only paths (render, dispatch, back, Salir,
/// invalid input, Ctrl+C) never dereference the store, so a null store is passed;
/// the store-backed operations are exercised at the integration layer
/// (Engram.Store.Tests / SyncBehaviorTests) and by the CLI handlers themselves.
/// </summary>
public sealed class InteractiveMenuTests
{
    // ─── Main menu render ──────────────────────────────────────────────────────

    /// <summary>
    /// The root menu renders the four required sections: Status, Sync, Proyectos, Salir.
    /// </summary>
    [Fact]
    public void Run_RendersMainMenu_WithFourSections()
    {
        using var output = new StringWriter();
        var input = new StringReader("4\n"); // Salir

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("=== Engram Interactive ===", text, StringComparison.Ordinal);
        Assert.Contains("1. Status", text, StringComparison.Ordinal);
        Assert.Contains("2. Sync", text, StringComparison.Ordinal);
        Assert.Contains("3. Proyectos", text, StringComparison.Ordinal);
        Assert.Contains("4. Salir", text, StringComparison.Ordinal);
    }

    // ─── Numeric input dispatch ────────────────────────────────────────────────

    /// <summary>
    /// Choosing "1" navigates into the Status submenu.
    /// </summary>
    [Fact]
    public void Run_NumericChoice_DispatchesToSubmenu()
    {
        using var output = new StringWriter();
        // "1" → Status submenu; "0" → Volver; "4" → Salir
        var input = new StringReader("1\n0\n4\n");

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("=== Status ===", output.ToString(), StringComparison.Ordinal);
    }

    // ─── Submenu navigation + back option ──────────────────────────────────────

    /// <summary>
    /// Selecting "0" (Volver) inside a submenu returns to the parent menu.
    /// </summary>
    [Fact]
    public void Run_SubmenuBackOption_ReturnsToMainMenu()
    {
        using var output = new StringWriter();
        // "2" → Sync submenu; "0" → Volver (back); "4" → Salir
        var input = new StringReader("2\n0\n4\n");

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("=== Sync ===", text, StringComparison.Ordinal);
        // After backing out, the main menu is rendered again before Salir.
        var mainMenuRenders = CountOccurrences(text, "=== Engram Interactive ===");
        Assert.True(mainMenuRenders >= 2, $"Expected main menu re-render after back, found {mainMenuRenders}.");
    }

    // ─── Salir exit ────────────────────────────────────────────────────────────

    /// <summary>
    /// Selecting "4" (Salir) exits cleanly with exit code 0.
    /// </summary>
    [Fact]
    public void Run_Salir_ExitsCleanly()
    {
        using var output = new StringWriter();
        var input = new StringReader("4\n");

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Exiting...", output.ToString(), StringComparison.Ordinal);
    }

    // ─── Invalid input handling ────────────────────────────────────────────────

    /// <summary>
    /// A non-menu choice prints an error and keeps the loop alive (no crash).
    /// </summary>
    [Fact]
    public void Run_InvalidInput_ShowsErrorAndContinues()
    {
        using var output = new StringWriter();
        // "99" → invalid; "4" → Salir
        var input = new StringReader("99\n4\n");

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Invalid option", output.ToString(), StringComparison.Ordinal);
    }

    // ─── Ctrl+C clean exit ─────────────────────────────────────────────────────

    /// <summary>
    /// Ctrl+C (with CancelKeyPress e.Cancel=true) interrupts Console.In.ReadLine()
    /// and returns null — the same EOF path a StringReader produces when exhausted.
    /// This asserts the interrupted read exits cleanly (exit code 0, no exception).
    /// </summary>
    [Fact]
    public void Run_InterruptedInput_ExitsCleanly()
    {
        using var output = new StringWriter();
        var input = new StringReader(""); // immediately EOF → simulates Ctrl+C interruption

        var exitCode = InteractiveMenu.Run(null!, input, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Exiting...", output.ToString(), StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
