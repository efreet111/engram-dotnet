namespace Engram.Obsidian;

/// <summary>
/// Extracts the prefix from a topic_key.
/// Port of topicPrefix() from Go's internal/obsidian/markdown.go.
///
/// Examples:
///   "auth/jwt"              → "auth"
///   "sdd/obsidian-plugin/explore" → "sdd/obsidian-plugin"
///   "standalone"            → "standalone"
/// </summary>
public static class TopicPrefix
{
    /// <summary>
    /// Returns everything before the last "/" in topicKey,
    /// or the whole string if no "/" is present.
    /// </summary>
    public static string Extract(string topicKey)
    {
        if (string.IsNullOrEmpty(topicKey))
            return "";

        var idx = topicKey.LastIndexOf('/');
        return idx < 0 ? topicKey : topicKey[..idx];
    }
}
