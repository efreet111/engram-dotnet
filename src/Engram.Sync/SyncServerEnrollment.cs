using System.Net.Http;
using System.Text;

namespace Engram.Sync;

/// <summary>
/// HU-065 F2: Server enrollment helpers for sync enroll command.
/// Exposed publicly so Engram.Cli and Engram.Sync.Tests can use it.
/// </summary>
public static class SyncServerEnrollment
{
    /// <summary>
    /// Makes POST /sync/enroll to the remote server.
    /// Best-effort: returns false on any failure without throwing.
    /// </summary>
    public static async Task<bool> EnrollProjectOnServerAsync(
        string serverUrl, string project, CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var user = Environment.GetEnvironmentVariable("ENGRAM_USER") ?? "anonymous";
            client.DefaultRequestHeaders.Add("X-Engram-User", user);

            var body = new ServerEnrollmentRequest(project);
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

            var response = await client.PostAsync(
                $"{serverUrl.TrimEnd('/')}/sync/enroll", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Request body for POST /sync/enroll
    /// </summary>
    public sealed record ServerEnrollmentRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("project")] string Project);
}
