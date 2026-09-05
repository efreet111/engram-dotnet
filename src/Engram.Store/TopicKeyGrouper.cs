namespace Engram.Store;

/// <summary>
/// Groups observations by topic_key for decision-tree and grouped-search displays.
/// Pure logic — no DB access. Shared by mem_decision_tree and mem_search --grouped.
/// ENG-412.
/// </summary>
public static class TopicKeyGrouper
{
    /// <summary>
    /// Groups observations by topic_key. Observations with null/empty topic_key
    /// are placed under the key "(no topic)". Within each group, observations are
    /// ordered by created_at DESC (most recent first).
    /// </summary>
    public static Dictionary<string, List<Observation>> GroupByTopicKey(
        IEnumerable<Observation> observations)
    {
        return observations
            .GroupBy(o => string.IsNullOrEmpty(o.TopicKey) ? "(no topic)" : o.TopicKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(o => o.CreatedAt).ToList());
    }

    /// <summary>
    /// Returns the most recent (active) observation per group, plus a count
    /// of deprecated/deleted observations in the same group.
    /// </summary>
    public static List<(Observation Head, int DeprecatedCount, int TotalCount)> GetGroupHeads(
        IEnumerable<Observation> observations)
    {
        var groups = GroupByTopicKey(observations);
        return groups.Values.Select(group =>
        {
            var head = group.First(); // already sorted DESC by CreatedAt
            var deprecatedCount = group.Count(o => o.Status == "deprecated");
            return (Head: head, DeprecatedCount: deprecatedCount, TotalCount: group.Count);
        }).ToList();
    }
}
