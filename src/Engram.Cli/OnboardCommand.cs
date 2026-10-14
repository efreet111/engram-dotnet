using System.CommandLine;
using System.Text;
using System.Text.Json;
using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// HU-055 (ENG-485): `engram onboard` — generates an onboarding summary for a new team member
/// from the team's memories in engram.
///
/// Sections: Top 10 Decisions, Active Conventions, Known Blockers/Gotchas,
/// Recent Insights, Where to Start.
/// </summary>
public static class OnboardCommand
{
    private const double TypeWeightDecision = 3.0;
    private const double TypeWeightInsight = 2.0;
    private const double TypeWeightConvention = 2.0;
    private const double TypeWeightBlocker = 1.5;
    private const double TypeWeightGotcha = 1.5;
    private const double TypeWeightManual = 1.0;

    /// <summary>Builds the <c>onboard</c> command with an optional injected store (for testing).</summary>
    /// <param name="storeFactory">Optional factory to create the IStore. When null, uses OpenStore().</param>
    public static Command CreateCommand(Func<IStore>? storeFactory = null)
    {
        var onboardCmd = new Command("onboard", "Generate onboarding summary for a new team member");
        var userArg = new Argument<string>("user") { Description = "Handle/username of the new developer" };
        var formatOpt = new Option<string>("--format")
        {
            Description = "Output format: markdown (default) or json"
        };
        var daysOpt = new Option<int>("--days")
        {
            Description = "Window for recent insights in days (default: 30)"
        };
        var projectOpt = new Option<string?>("--project")
        {
            Description = "Filter by project name"
        };
        var outputOpt = new Option<string?>("--output")
        {
            Description = "Write output to file instead of stdout"
        };

        onboardCmd.Arguments.Add(userArg);
        onboardCmd.Options.Add(formatOpt);
        onboardCmd.Options.Add(daysOpt);
        onboardCmd.Options.Add(projectOpt);
        onboardCmd.Options.Add(outputOpt);

        onboardCmd.SetAction(async (ParseResult parseResult) =>
        {
            var user = parseResult.GetValue(userArg)!;
            var format = parseResult.GetValue(formatOpt) ?? "markdown";
            var days = parseResult.GetValue(daysOpt);
            var project = parseResult.GetValue(projectOpt);
            var outputFile = parseResult.GetValue(outputOpt);

            if (format != "markdown" && format != "json")
            {
                Console.Error.WriteLine($"error: --format must be 'markdown' or 'json', got '{format}'");
                return 1;
            }

            using var store = (storeFactory ?? OpenStore)();

            OnboardingReport report;
            try
            {
                report = await GenerateOnboardingReportAsync(store, user, project, days);
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }

            string content = format == "json"
                ? FormatJson(report)
                : FormatMarkdown(report, user);

            if (!string.IsNullOrEmpty(outputFile))
            {
                try
                {
                    var dir = Path.GetDirectoryName(outputFile);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(outputFile, content);
                    Console.WriteLine($"Onboarding summary written to: {outputFile}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: failed to write output file: {ex.Message}");
                    return 1;
                }
                return 0;
            }

            Console.WriteLine(content);
            return 0;
        });

        return onboardCmd;
    }

    /// <summary>
    /// Generates the full onboarding report by querying the store.
    /// </summary>
    internal static async Task<OnboardingReport> GenerateOnboardingReportAsync(
        IStore store, string user, string? project, int days)
    {
        // Default days to 30 if not specified
        if (days <= 0) days = 30;

        var decisions = await store.GetTopDecisionsAsync(10, project, days);
        var conventions = await store.GetActiveConventionsAsync(20, project, days);
        var blockers = await store.GetBlockersAsync(project);
        var insights = await store.GetRecentInsightsAsync(days, project, 20);
        var whereToStart = await store.GetMostReferencedConceptsAsync(10, project);

        // Calculate total memories (approximate via stats)
        var stats = await store.StatsAsync();

        // Calculate importance for each item
        var decisionItems = decisions.Select(r => new OnboardingItem
        {
            Id = r.Observation.Id,
            Type = r.Observation.Type,
            Title = r.Observation.Title,
            Content = r.Observation.Content,
            CreatedAt = r.Observation.CreatedAt,
            Project = r.Observation.Project,
            Importance = r.Rank, // pre-computed in the store query
        }).ToList();

        var conventionItems = conventions.Select(r => new OnboardingItem
        {
            Id = r.Observation.Id,
            Type = r.Observation.Type,
            Title = r.Observation.Title,
            Content = r.Observation.Content,
            CreatedAt = r.Observation.CreatedAt,
            Project = r.Observation.Project,
            Importance = CalculateImportance(r.Observation),
        }).ToList();

        var blockerItems = blockers.Select(r => new OnboardingItem
        {
            Id = r.Observation.Id,
            Type = r.Observation.Type,
            Title = r.Observation.Title,
            Content = r.Observation.Content,
            CreatedAt = r.Observation.CreatedAt,
            Project = r.Observation.Project,
            Importance = CalculateImportance(r.Observation),
        }).ToList();

        var insightItems = insights.Select(r => new OnboardingItem
        {
            Id = r.Observation.Id,
            Type = r.Observation.Type,
            Title = r.Observation.Title,
            Content = r.Observation.Content,
            CreatedAt = r.Observation.CreatedAt,
            Project = r.Observation.Project,
            Importance = CalculateImportance(r.Observation),
        }).ToList();

        return new OnboardingReport
        {
            User = user,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            TotalMemories = stats.TotalObservations,
            Sections = new OnboardingSections
            {
                Decisions = decisionItems,
                Conventions = conventionItems,
                Blockers = blockerItems,
                Insights = insightItems,
                WhereToStart = whereToStart.ToList(),
            },
        };
    }

    private static double CalculateImportance(Observation obs)
    {
        var typeWeight = obs.Type.ToLowerInvariant() switch
        {
            "decision" => TypeWeightDecision,
            "insight" => TypeWeightInsight,
            "convention" => TypeWeightConvention,
            "blocker" => TypeWeightBlocker,
            "gotcha" => TypeWeightGotcha,
            _ => TypeWeightManual,
        };

        double recencyScore = 0.2;
        if (DateTime.TryParse(obs.CreatedAt, out var created))
        {
            var age = DateTime.UtcNow - created;
            if (age.TotalDays < 30) recencyScore = 1.0;
            else if (age.TotalDays < 60) recencyScore = 0.7;
            else if (age.TotalDays < 90) recencyScore = 0.4;
        }

        return recencyScore * typeWeight;
    }

    /// <summary>Formats the report as human-readable markdown.</summary>
    internal static string FormatMarkdown(OnboardingReport report, string user)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Welcome to the team, {user}! 🎉");
        sb.AppendLine();
        sb.AppendLine("Here's what you need to know based on our team memory:");
        sb.AppendLine();
        sb.AppendLine($"_Generated: {report.GeneratedAt} | Total memories: {report.TotalMemories}_");
        sb.AppendLine();

        // Section 1: Top 10 Decisions
        sb.AppendLine("## 🏗️ Top 10 Architectural Decisions");
        if (report.Sections.Decisions.Count == 0)
        {
            sb.AppendLine("_No decisions captured yet. Start capturing architectural decisions with `engram save`._");
        }
        else
        {
            for (int i = 0; i < report.Sections.Decisions.Count; i++)
            {
                var d = report.Sections.Decisions[i];
                sb.AppendLine($"{i + 1}. **{d.Title}**");
                sb.AppendLine($"   {Truncate(d.Content, 200)}");
                if (!string.IsNullOrEmpty(d.Project))
                    sb.AppendLine($"   _project: {d.Project} | {d.CreatedAt}_");
                else
                    sb.AppendLine($"   _{d.CreatedAt}_");
                sb.AppendLine();
            }
        }
        sb.AppendLine();

        // Section 2: Active Conventions
        sb.AppendLine("## 📏 Active Conventions");
        if (report.Sections.Conventions.Count == 0)
        {
            sb.AppendLine("_No conventions captured yet. Start documenting team conventions with `engram save --type convention`._");
        }
        else
        {
            foreach (var c in report.Sections.Conventions.Take(10))
            {
                sb.AppendLine($"- **{c.Title}**");
                sb.AppendLine($"  {Truncate(c.Content, 150)}");
            }
        }
        sb.AppendLine();

        // Section 3: Known Blockers / Gotchas
        sb.AppendLine("## 🚧 Known Blockers / Gotchas");
        if (report.Sections.Blockers.Count == 0)
        {
            sb.AppendLine("_No blockers or gotchas documented. That's the good path!_");
        }
        else
        {
            foreach (var b in report.Sections.Blockers)
            {
                sb.AppendLine($"- **[{b.Type.ToUpper()}]** {b.Title}");
                sb.AppendLine($"  {Truncate(b.Content, 150)}");
            }
        }
        sb.AppendLine();

        // Section 4: Recent Insights
        sb.AppendLine($"## 💡 Recent Insights (last 30 days)");
        if (report.Sections.Insights.Count == 0)
        {
            sb.AppendLine("_No recent insights captured. Start capturing insights with `engram save --type insight`._");
        }
        else
        {
            foreach (var ins in report.Sections.Insights.Take(10))
            {
                sb.AppendLine($"- **{ins.Title}**");
                sb.AppendLine($"  {Truncate(ins.Content, 120)}");
                sb.AppendLine($"  _{ins.CreatedAt}| type: {ins.Type}_");
            }
        }
        sb.AppendLine();

        // Section 5: Where to Start
        sb.AppendLine("## 🗺️ Where to Start");
        if (report.Sections.WhereToStart.Count == 0)
        {
            sb.AppendLine("_No code references yet. As you capture memories with `--file-path` or `--symbol`, this section will grow._");
        }
        else
        {
            sb.AppendLine("Most-referenced code locations in team memories:");
            foreach (var w in report.Sections.WhereToStart)
            {
                var label = w.Type switch
                {
                    "file_path" => "📄",
                    "symbol" => "🔣",
                    "namespace" => "📦",
                    _ => "•",
                };
                sb.AppendLine($"- {label} `{w.Concept}` — referenced {w.RefCount}x");
            }
        }

        return sb.ToString();
    }

    /// <summary>Formats the report as machine-readable JSON.</summary>
    internal static string FormatJson(OnboardingReport report)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        return JsonSerializer.Serialize(report, opts);
    }

    /// <summary>Truncates a string to maxLen characters, appending "..." if truncated.</summary>
    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
            return value;
        return value[..(maxLen - 3)] + "...";
    }

    /// <summary>
    /// Replicates the store-opening logic of Program.cs's local OpenStore.
    /// (the local function is not reusable from here). Thin-client → HttpStore, else
    /// Sqlite/Postgres by StoreDbType. Fail-loud on invalid profile/connection.
    /// </summary>
    private static IStore OpenStore()
    {
        var cfg = StoreConfig.FromEnvironment();
        ProfileValidator.Validate(cfg);

        if (cfg.IsThinClient)
            return new HttpStore(cfg);

        if (cfg.IsPostgres && string.IsNullOrWhiteSpace(cfg.PgConnectionString))
            throw new InvalidOperationException("ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres");

        return cfg.DbType switch
        {
            StoreDbType.Postgres => new PostgresStore(cfg),
            _ => new SqliteStore(cfg),
        };
    }
}
