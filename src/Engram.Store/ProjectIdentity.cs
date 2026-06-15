using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Engram.Store;

/// <summary>
/// Project identity fingerprint — UUID v5 determinista desde URL de origen + hash del primer commit.
/// RFC-001 / ENG-410.
/// </summary>
public static class ProjectIdentity
{
    private const string IdFileName = ".engram-id";

    /// <summary>
    /// Obtiene el ID del proyecto desde archivo .engram-id.
    /// Retorna null si el archivo no existe o contiene un GUID inválido.
    /// </summary>
    public static string? GetProjectId(string repoPath)
    {
        var idFile = Path.Combine(repoPath, IdFileName);
        if (File.Exists(idFile))
        {
            var content = File.ReadAllText(idFile).Trim();
            if (Guid.TryParse(content, out _))
                return content;
        }

        return null;
    }

    /// <summary>
    /// Calcula UUID v5 determinista desde la URL del remote + hash del primer commit.
    /// </summary>
    public static Guid ComputeProjectId(string originUrl, string firstCommitSha)
    {
        var normalizedUrl = NormalizeUrl(originUrl);
        var fingerprint = $"{normalizedUrl}|{firstCommitSha}";
        return CreateGuidV5(GuidNamespaces.Url, fingerprint);
    }

    /// <summary>
    /// Guarda .engram-id en la raíz del repo.
    /// </summary>
    public static void SaveProjectId(string repoPath, Guid projectId)
    {
        var idFile = Path.Combine(repoPath, IdFileName);
        File.WriteAllText(idFile, projectId.ToString("D"));
    }

    /// <summary>
    /// Normaliza URL: quita schema, auth, .git suffix, normaliza a lowercase.
    /// "https://github.com/User/Repo.git" → "github.com/user/repo"
    /// "git@github.com:User/Repo" → "github.com/user/repo"
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        var result = url.Trim();

        // Remove git@ prefix
        result = Regex.Replace(result, @"^git@", "");

        // Replace : with / (for git@ URLs)
        result = Regex.Replace(result, @"\.com:", ".com/");

        // Remove http(s)://
        result = Regex.Replace(result, @"^https?://", "");

        // Remove trailing .git
        if (result.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            result = result[..^4];

        // Remove trailing /
        result = result.TrimEnd('/');

        return result.ToLowerInvariant();
    }

    /// <summary>
    /// Obtiene el hash del primer commit del repositorio git.
    /// </summary>
    public static string? GetFirstCommitSha(string repoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{repoPath}\" rev-list --max-parents=0 --reverse HEAD",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return null;
            }
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            // Take the first line (first commit could have multiple parents in merge commits)
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a UUID v5 (SHA-1 based) using the given namespace and name.
    /// </summary>
    private static Guid CreateGuidV5(Guid namespaceId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var namespaceBytes = namespaceId.ToByteArray();

        // UUID v5 uses SHA-1
        using var sha1 = SHA1.Create();

        // Combine namespace bytes + name bytes
        var combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Array.Copy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Array.Copy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

        var hash = sha1.ComputeHash(combined);

        // Set version (5) in the version field (bits 12-15)
        hash[7] &= 0x0f;
        hash[7] |= 0x50;

        // Set variant (RFC 4122) in the variant field (bits 14-15)
        hash[8] &= 0x3f;
        hash[8] |= 0x80;

        return new Guid(hash[..16]);
    }
}

/// <summary>
/// Well-known UUID namespaces for UUID v3/v5.
/// </summary>
public static class GuidNamespaces
{
    /// <summary>
    /// Namespace for URLs (RFC 4122).
    /// </summary>
    public static readonly Guid Url = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// Namespace for DNS (RFC 4122).
    /// </summary>
    public static readonly Guid Dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
}