using System.CommandLine;
using System.Text.Json;
using Engram.Cli;
using Engram.Store;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-023: Shared command-tree builder mirroring the `profile` block in Program.cs.
/// Program.cs is a top-level-statement binary (not referenceable), so the existing CLI
/// test pattern rebuilds the command tree and invokes it via <c>InvokeAsync</c>. Keeping
/// the mirror in one place prevents drift between ProfileShowTests and ProfileSetTests.
/// Handlers delegate to <see cref="ProfileConfig"/> — the same shared logic Program.cs uses.
/// </summary>
internal static class ProfileCommandTree
{
    public static RootCommand Build()
    {
        var profileCmd = new Command("profile", "Show or set the deployment profile");

        // profile show
        var showCmd = new Command("show", "Show the active deployment profile and effective variables");
        var showJsonOpt = new Option<bool>("--json") { Description = "Output as JSON" };
        showCmd.Options.Add(showJsonOpt);
        showCmd.SetAction((ParseResult parseResult) =>
        {
            var json = parseResult.GetValue(showJsonOpt);
            var raw = Environment.GetEnvironmentVariable(ProfileConfig.ProfileEnvVar);
            var isDefault = string.IsNullOrWhiteSpace(raw);
            var profile = DeployProfileExtensions.FromEnvironment();
            var label = profile.ToLabel();

            var envPath = ProfileConfig.ResolveEnvPath();
            var configExists = File.Exists(envPath);
            var fileProfile = configExists ? ProfileConfig.ReadProfileFromFile(envPath) : null;

            if (json)
            {
                var variables = ProfileConfig.GetEffectiveVariables(profile)
                    .ToDictionary(v => v.Key, v => new { value = v.Value, source = v.Source });
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    profile = label,
                    is_default = isDefault,
                    config_file = envPath,
                    config_file_exists = configExists,
                    config_file_profile = fileProfile,
                    variables,
                }));
                return;
            }

            Console.WriteLine(isDefault ? $"Profile: {label} (default)" : $"Profile: {label}");
            Console.WriteLine(configExists
                ? $"Config file: {envPath} (declares: {fileProfile ?? "unset"})"
                : $"Config file: {envPath} (not found — using environment variables)");
            Console.WriteLine("Effective variables:");
            foreach (var (key, value, source) in ProfileConfig.GetEffectiveVariables(profile))
                Console.WriteLine($"  {key,-28} {value}  [{source}]");
        });

        // profile set
        var setCmd = new Command("set", "Set the deployment profile (writes ~/.engram/.env)");
        var setNameArg = new Argument<string>("profile") { Description = "Profile name: local, remote-server, offline-first, desktop" };
        var setDryRunOpt = new Option<bool>("--dry-run") { Description = "Preview changes without writing files" };
        var setJsonOpt = new Option<bool>("--json") { Description = "Output as JSON" };
        setCmd.Arguments.Add(setNameArg);
        setCmd.Options.Add(setDryRunOpt);
        setCmd.Options.Add(setJsonOpt);
        setCmd.SetAction((ParseResult parseResult) =>
        {
            var name = parseResult.GetValue(setNameArg)!;
            var dryRun = parseResult.GetValue(setDryRunOpt);
            var json = parseResult.GetValue(setJsonOpt);

            DeployProfile target;
            try
            {
                target = ProfileConfig.ParseProfileName(name);
            }
            catch (InvalidOperationException ex)
            {
                if (json) Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }));
                else Console.Error.WriteLine($"error: {ex.Message}");
                return;
            }

            var label = target.ToLabel();
            var envPath = ProfileConfig.ResolveEnvPath();
            var backupPath = ProfileConfig.BackupPath(envPath);
            var alreadySet = ProfileConfig.IsProfileAlreadySet(target, envPath);
            var missing = ProfileValidator.GetMissingVariables(ProfileConfig.StoreConfigForProfile(target));

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    profile = label,
                    changed = !alreadySet && !dryRun,
                    dry_run = dryRun,
                    already_set = alreadySet,
                    env_file = envPath,
                    backup_file = backupPath,
                    missing_variables = missing.ToArray(),
                }));
                return;
            }

            if (alreadySet)
            {
                Console.WriteLine($"Profile already set to '{label}'. No changes made.");
                return;
            }

            if (dryRun)
            {
                Console.WriteLine($"[DRY RUN] Would set profile to '{label}'");
                Console.WriteLine($"  Write:   {envPath}");
                Console.WriteLine($"  Backup:  {backupPath}{(File.Exists(envPath) ? " (existing .env)" : " (no existing .env)")}");
                if (missing.Count > 0)
                    Console.WriteLine($"  Missing required vars: {string.Join(", ", missing)}");
                return;
            }

            ProfileConfig.WriteProfile(target, envPath);
            Console.WriteLine($"Profile set to '{label}'.");
            Console.WriteLine($"  Wrote:   {envPath}");
            Console.WriteLine($"  Backup:  {backupPath}");
            if (missing.Count > 0)
                Console.WriteLine($"  Note: profile requires: {string.Join(", ", missing)} — set them before starting the server.");
        });

        profileCmd.Subcommands.Add(showCmd);
        profileCmd.Subcommands.Add(setCmd);

        var root = new RootCommand();
        root.Subcommands.Add(profileCmd);
        return root;
    }
}
