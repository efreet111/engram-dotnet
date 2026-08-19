using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// HU-018: Interactive TUI navigator for sync and project operations.
/// Zero-dependency menu loop (Console.WriteLine + ReadLine) — no fzf, ncurses, or terminal.gui.
/// I/O is injectable (<see cref="TextReader"/>/<see cref="TextWriter"/>) so navigation can be
/// unit-tested with StringReader/StringWriter (AD5).
/// </summary>
public static class InteractiveMenu
{
    /// <summary>
    /// Runs the interactive menu loop until the user selects "Salir" or interrupts with Ctrl+C.
    /// Returns 0 on clean exit.
    /// </summary>
    /// <param name="store">The backing store (ownership/disposal remains with the caller).</param>
    /// <param name="input">Injectable input stream (<c>Console.In</c> in production).</param>
    /// <param name="output">Injectable output stream (<c>Console.Out</c> in production).</param>
    public static int Run(IStore store, TextReader input, TextWriter output)
    {
        // Suppress the default Ctrl+C handler so the process exits cleanly (return 0)
        // instead of printing a stack trace / terminating with a signal. When Cancel is
        // true, a blocking Console.In.ReadLine() is interrupted and returns null, which the
        // loop below treats as a clean exit request.
        ConsoleCancelEventHandler cancelHandler = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return RunLoop(store, input, output);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Main menu loop: renders the menu, reads a numeric choice, and dispatches to the
    /// matching submenu. Loops until "Salir" or an interrupted read (Ctrl+C / EOF).
    /// </summary>
    private static int RunLoop(IStore store, TextReader input, TextWriter output)
    {
        while (true)
        {
            ShowMainMenu(output);

            var choice = ReadChoice(input, output);
            if (choice is null)
            {
                // EOF (Ctrl+D) or Ctrl+C interrupted the read → clean exit.
                output.WriteLine();
                output.WriteLine("Exiting...");
                return 0;
            }

            switch (choice)
            {
                case "1":
                    if (ShowStatusMenu(store, input, output)) return 0;
                    break;
                case "2":
                    if (ShowSyncMenu(store, input, output)) return 0;
                    break;
                case "3":
                    if (ShowProjectsMenu(store, input, output)) return 0;
                    break;
                case "4":
                case "salir":
                case "exit":
                    output.WriteLine("Exiting...");
                    return 0;
                case "":
                    // Blank input → re-prompt silently (not an error).
                    break;
                default:
                    output.WriteLine("Invalid option");
                    break;
            }
        }
    }

    /// <summary>
    /// Renders the top-level menu with the four sections: Status, Sync, Proyectos, Salir.
    /// </summary>
    private static void ShowMainMenu(TextWriter output)
    {
        output.WriteLine("=== Engram Interactive ===");
        output.WriteLine("1. Status");
        output.WriteLine("2. Sync");
        output.WriteLine("3. Proyectos");
        output.WriteLine("4. Salir");
        output.WriteLine();
    }

    /// <summary>
    /// Status submenu: local sync status and project list.
    /// Returns true when the read was interrupted (Ctrl+C / EOF) so the caller exits cleanly.
    /// </summary>
    private static bool ShowStatusMenu(IStore store, TextReader input, TextWriter output)
    {
        while (true)
        {
            output.WriteLine();
            output.WriteLine("=== Status ===");
            output.WriteLine("1. Sync status (--local)");
            output.WriteLine("2. Proyectos");
            output.WriteLine("0. Volver");
            output.WriteLine();

            var choice = ReadChoice(input, output);
            if (choice is null) return true;

            switch (choice)
            {
                case "1":
                    ShowLocalSyncStatus(store, output);
                    break;
                case "2":
                    ShowProjectsList(store, output);
                    break;
                case "0":
                case "":
                case "volver":
                case "back":
                    return false;
                default:
                    output.WriteLine("Invalid option");
                    break;
            }
        }
    }

    /// <summary>
    /// Sync submenu: batch enroll, batch push, and local sync status.
    /// Returns true when the read was interrupted (Ctrl+C / EOF) so the caller exits cleanly.
    /// </summary>
    private static bool ShowSyncMenu(IStore store, TextReader input, TextWriter output)
    {
        while (true)
        {
            output.WriteLine();
            output.WriteLine("=== Sync ===");
            output.WriteLine("1. Enroll --all");
            output.WriteLine("2. Push --all");
            output.WriteLine("3. Sync status (--local)");
            output.WriteLine("0. Volver");
            output.WriteLine();

            var choice = ReadChoice(input, output);
            if (choice is null) return true;

            switch (choice)
            {
                case "1":
                    EnrollAll(store, output);
                    break;
                case "2":
                    PushAll(store, output);
                    break;
                case "3":
                    ShowLocalSyncStatus(store, output);
                    break;
                case "0":
                case "":
                case "volver":
                case "back":
                    return false;
                default:
                    output.WriteLine("Invalid option");
                    break;
            }
        }
    }

    /// <summary>
    /// Proyectos submenu: project list, consolidate preview, and prune preview.
    /// Returns true when the read was interrupted (Ctrl+C / EOF) so the caller exits cleanly.
    /// </summary>
    private static bool ShowProjectsMenu(IStore store, TextReader input, TextWriter output)
    {
        while (true)
        {
            output.WriteLine();
            output.WriteLine("=== Proyectos ===");
            output.WriteLine("1. List");
            output.WriteLine("2. Consolidar (vista previa)");
            output.WriteLine("3. Prune (vista previa)");
            output.WriteLine("0. Volver");
            output.WriteLine();

            var choice = ReadChoice(input, output);
            if (choice is null) return true;

            switch (choice)
            {
                case "1":
                    ShowProjectsList(store, output);
                    break;
                case "2":
                    ShowConsolidatePreview(store, output);
                    break;
                case "3":
                    ShowPrunePreview(store, output);
                    break;
                case "0":
                case "":
                case "volver":
                case "back":
                    return false;
                default:
                    output.WriteLine("Invalid option");
                    break;
            }
        }
    }

    /// <summary>
    /// Writes the "&gt; " prompt and reads a single choice line from the injected input.
    /// Returns the trimmed choice, or null when the stream is exhausted / interrupted.
    /// </summary>
    private static string? ReadChoice(TextReader input, TextWriter output)
    {
        output.Write("> ");
        output.Flush();
        return input.ReadLine()?.Trim();
    }

    // ─── Operations ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows local enrollment status for all projects (no server required).
    /// Union of enrolled projects, pending mutation counts, and project stats.
    /// Mirrors the `sync status --local` output (task 2.2) but writes to the injected writer.
    /// </summary>
    private static void ShowLocalSyncStatus(IStore store, TextWriter output)
    {
        if (store is not ILocalSyncStore localStore)
        {
            output.WriteLine("error: local sync status is only supported for local SQLite stores.");
            return;
        }

        var enrolled = localStore.GetEnrolledProjectsLocalAsync().GetAwaiter().GetResult();
        var pendingCounts = localStore.CountPendingMutationsByProjectAsync("cloud").GetAwaiter().GetResult();
        var allStats = store.ListProjectsWithStatsAsync().GetAwaiter().GetResult();

        var behaviorByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ep in enrolled)
            behaviorByProject[ep.Project] = ep.Behavior;

        var pendingByProject = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pc in pendingCounts)
            pendingByProject[pc.Project] = pc.Count;

        var projectNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in allStats) projectNames.Add(s.Name);
        foreach (var ep in enrolled) projectNames.Add(ep.Project);
        foreach (var pc in pendingCounts) projectNames.Add(pc.Project);

        if (projectNames.Count == 0)
        {
            output.WriteLine("No projects found.");
            return;
        }

        output.WriteLine($"Local sync status ({projectNames.Count} projects):");
        foreach (var name in projectNames)
        {
            var isEnrolled = behaviorByProject.TryGetValue(name, out var behavior);
            var pending = pendingByProject.TryGetValue(name, out var count) ? count : 0L;
            var enrolledMark = isEnrolled ? "✓" : "—";
            var behaviorLabel = isEnrolled ? behavior : "";
            output.WriteLine($"  {name,-30} {enrolledMark}  {behaviorLabel,-12} {pending,4} pending");
        }
    }

    /// <summary>
    /// Lists all projects with observation/session/prompt stats plus enrollment columns.
    /// Mirrors the `projects list` output (task 2.4) but writes to the injected writer.
    /// </summary>
    private static void ShowProjectsList(IStore store, TextWriter output)
    {
        var stats = store.ListProjectsWithStatsAsync().GetAwaiter().GetResult();
        if (stats.Count == 0)
        {
            output.WriteLine("No projects found.");
            return;
        }

        // Enrollment lookup is gated to ILocalSyncStore — remote stores render as unenrolled.
        var behaviors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (store is ILocalSyncStore localStore)
        {
            var enrolled = localStore.GetEnrolledProjectsLocalAsync().GetAwaiter().GetResult();
            foreach (var ep in enrolled)
                behaviors[ep.Project] = ep.Behavior;
        }

        output.WriteLine($"Projects ({stats.Count}):");
        output.WriteLine();
        output.WriteLine($"  {"Name",-30}  {"Obs",4}  {"Sessions",8}  {"Prompts",7}  {"Enrolled",-8}  {"Behavior",-15}");
        output.WriteLine($"  {new string('─', 30)}  {new string('─', 3)}  {new string('─', 8)}  {new string('─', 7)}  {new string('─', 8)}  {new string('─', 15)}");
        foreach (var p in stats)
        {
            var enrolled = behaviors.TryGetValue(p.Name, out var behavior);
            var enrolledMark = enrolled ? "✓" : "—";
            var behaviorLabel = enrolled ? $"({behavior})" : "";
            output.WriteLine($"  {p.Name,-30}  {p.ObservationCount,4}  {p.SessionCount,8}  {p.PromptCount,7}  {enrolledMark,-8}  {behaviorLabel,-15}");
        }
    }

    /// <summary>
    /// Basic batch enrollment: enrolls all projects with pending mutations that are not
    /// yet locally enrolled (default fail-loud). Full `enroll --all` (which also pushes)
    /// is implemented in task 2.1.
    /// </summary>
    private static void EnrollAll(IStore store, TextWriter output)
    {
        if (store is not ILocalSyncStore localStore)
        {
            output.WriteLine("error: enrollment is only supported for local SQLite stores.");
            return;
        }

        var pendingProjects = localStore.ListDistinctProjectsWithPendingMutationsAsync("cloud").GetAwaiter().GetResult();
        if (pendingProjects.Count == 0)
        {
            output.WriteLine("No projects with pending mutations found.");
            return;
        }

        var enrolled = localStore.GetEnrolledProjectsLocalAsync().GetAwaiter().GetResult();
        var enrolledSet = new HashSet<string>(
            enrolled.Select(e => e.Project), StringComparer.Ordinal);

        var missing = pendingProjects.Where(p => !enrolledSet.Contains(p)).ToList();
        foreach (var p in missing)
        {
            localStore.EnrollProjectLocalAsync(p, "fail-loud").GetAwaiter().GetResult();
            output.WriteLine($"  Enrolled '{p}' (behavior: fail-loud).");
        }

        output.WriteLine(missing.Count == 0
            ? "All projects with pending mutations are already enrolled."
            : $"Enrolled {missing.Count} project(s) with pending mutations.");
    }

    /// <summary>
    /// Basic batch push summary: reports pending mutation counts per enrolled project
    /// with active behavior. The actual transport push lives in the `sync push --all`
    /// command (shared PushProjectAsync helper, tasks 1.1/1.2).
    /// </summary>
    private static void PushAll(IStore store, TextWriter output)
    {
        if (store is not ILocalSyncStore localStore)
        {
            output.WriteLine("error: push is only supported for local SQLite stores.");
            return;
        }

        var enrolled = localStore.GetEnrolledProjectsLocalAsync().GetAwaiter().GetResult();
        var active = enrolled.Where(ep => ep.Behavior != "silent-skip").ToList();
        if (active.Count == 0)
        {
            output.WriteLine("No enrolled projects with active push behavior.");
            return;
        }

        var pendingCounts = localStore.CountPendingMutationsByProjectAsync("cloud").GetAwaiter().GetResult();
        var pendingByProject = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pc in pendingCounts)
            pendingByProject[pc.Project] = pc.Count;

        output.WriteLine("Pending mutations per enrolled project:");
        long total = 0;
        foreach (var ep in active)
        {
            var pending = pendingByProject.TryGetValue(ep.Project, out var count) ? count : 0L;
            total += pending;
            output.WriteLine($"  {ep.Project,-30} {pending,4} pending  ({ep.Behavior})");
        }

        output.WriteLine($"Total: {total} pending mutation(s) across {active.Count} project(s).");
        output.WriteLine("Use 'engram sync push --all' to push them to the sync server.");
    }

    /// <summary>
    /// Read-only consolidate preview: groups similar project names without merging anything.
    /// </summary>
    private static void ShowConsolidatePreview(IStore store, TextWriter output)
    {
        var projects = store.ListProjectsWithStatsAsync().GetAwaiter().GetResult();
        var groups = ProjectConsolidator.GroupSimilarProjects(projects);
        if (groups.Count == 0)
        {
            output.WriteLine("No similar project name groups found.");
            return;
        }

        output.WriteLine($"Found {groups.Count} group(s) of similar project names:");
        foreach (var g in groups)
        {
            output.WriteLine($"  → {g.Canonical}");
            foreach (var name in g.Names)
                output.WriteLine($"      {name}");
        }

        output.WriteLine("Use 'engram projects consolidate' to merge them.");
    }

    /// <summary>
    /// Read-only prune preview: lists projects with 0 observations without deleting anything.
    /// </summary>
    private static void ShowPrunePreview(IStore store, TextWriter output)
    {
        var allStats = store.ListProjectsWithStatsAsync().GetAwaiter().GetResult();
        var candidates = allStats.Where(ps => ps.ObservationCount == 0).ToList();
        if (candidates.Count == 0)
        {
            output.WriteLine("No empty projects to prune.");
            return;
        }

        output.WriteLine($"Projects with 0 observations ({candidates.Count}):");
        for (int i = 0; i < candidates.Count; i++)
        {
            var ps = candidates[i];
            output.WriteLine($"  [{i + 1}] {ps.Name,-30} {ps.SessionCount,3} sessions  {ps.PromptCount,3} prompts");
        }

        output.WriteLine("Use 'engram projects prune' to remove them.");
    }
}
