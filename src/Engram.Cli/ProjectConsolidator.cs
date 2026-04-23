using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// Represents a set of project names that should be merged.
/// Mirrors the Go original's projectGroup type.
/// </summary>
public class ProjectGroup
{
    public List<string> Names { get; set; } = [];
    public string Canonical { get; set; } = "";
}

/// <summary>
/// Groups projects by name similarity (Levenshtein/case/substring) and
/// shared directories, using a union-find approach.
/// Mirrors the Go original's groupSimilarProjects function.
/// </summary>
public static class ProjectConsolidator
{
    public static List<ProjectGroup> GroupSimilarProjects(IList<ProjectStats> projects)
    {
        var n = projects.Count;
        if (n == 0) return [];

        // Union-Find
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(int x, int y) { var rx = Find(x); var ry = Find(y); if (rx != ry) parent[rx] = ry; }

        // Build names for FindSimilar
        var names = projects.Select(p => p.Name).ToList();
        var nameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++) nameToIndex[projects[i].Name] = i;

        // Group by name similarity
        for (int i = 0; i < n; i++)
        {
            var similar = ProjectDetector.FindSimilar(projects[i].Name, names, 3);
            foreach (var sm in similar)
            {
                if (nameToIndex.TryGetValue(sm.Name, out var j))
                    Union(i, j);
            }
        }

        // Group by shared directory
        var dirToProjects = new Dictionary<string, List<int>>();
        for (int i = 0; i < n; i++)
        {
            foreach (var dir in projects[i].Directories)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                if (!dirToProjects.TryGetValue(dir, out var list))
                {
                    list = [];
                    dirToProjects[dir] = list;
                }
                list.Add(i);
            }
        }
        foreach (var idxs in dirToProjects.Values)
            for (int k = 1; k < idxs.Count; k++)
                Union(idxs[0], idxs[k]);

        // Collect components
        var components = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!components.TryGetValue(root, out var list))
            {
                list = [];
                components[root] = list;
            }
            list.Add(i);
        }

        // Build groups — skip singletons (no duplicates)
        var groups = new List<ProjectGroup>();
        foreach (var idxs in components.Values)
        {
            if (idxs.Count < 2) continue;

            // Suggest the one with most observations as canonical
            var bestIdx = idxs[0];
            for (int k = 1; k < idxs.Count; k++)
                if (projects[idxs[k]].ObservationCount > projects[bestIdx].ObservationCount)
                    bestIdx = idxs[k];

            var grpNames = idxs.Select(i => projects[i].Name).OrderBy(name => name).ToList();
            groups.Add(new ProjectGroup { Names = grpNames, Canonical = projects[bestIdx].Name });
        }

        // Sort groups by canonical name for deterministic output
        groups.Sort((a, b) => string.Compare(a.Canonical, b.Canonical, StringComparison.Ordinal));
        return groups;
    }
}