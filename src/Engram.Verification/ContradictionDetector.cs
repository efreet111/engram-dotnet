using System.Text.Json.Serialization;
using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// Detects three types of contradictions in memory observations:
/// direct (explicit conflicts_with), temporal (newer observation on same topic),
/// and embedding (high text similarity but divergent content).
///
/// Follows <see cref="MemoryLineageBuilder"/> pattern: primary constructor,
/// readonly fields, injected dependencies.
/// </summary>
public sealed class ContradictionDetector : IDisposable
{
    private readonly IStore _store;
    private readonly MemoryRelationRepository _memRelRepo;
    private readonly string _defaultSessionId;

    public ContradictionDetector(IStore store, MemoryRelationRepository memRelRepo)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _memRelRepo = memRelRepo ?? throw new ArgumentNullException(nameof(memRelRepo));
        _defaultSessionId = $"contradiction-detector-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Detect observations linked by explicit conflicts_with relations.
    /// Returns only pairs where both observations are active.
    /// Confidence = 1.0 for all direct contradictions.
    /// </summary>
    public async Task<List<ContradictionResult>> DetectDirectContradictionsAsync(
        string project, double confidenceThreshold = 0.5, int limit = 50)
    {
        var results = new List<ContradictionResult>();

        // Get all active observations for the project
        var observations = await _store.RecentObservationsAsync(project, scope: null, limit: 1000);
        var activeObs = observations.Where(o => o.Status == "active").ToList();
        var seen = new HashSet<(long, long)>();

        foreach (var obs in activeObs)
        {
            var relations = await _memRelRepo.GetRelationsAsync(project, obs.Id);
            foreach (var rel in relations.Where(r => r.Type == "conflicts_with"))
            {
                var otherId = rel.TargetObservationId;
                if (otherId == obs.Id) continue;

                // Skip if target is not active
                var other = activeObs.FirstOrDefault(o => o.Id == otherId);
                if (other == null) continue;

                // Deduplicate pair (order-independent)
                var key = obs.Id < otherId ? (obs.Id, otherId) : (otherId, obs.Id);
                if (!seen.Add(key)) continue;

                if (1.0 >= confidenceThreshold)
                {
                    results.Add(new ContradictionResult
                    {
                        ObsIdA = obs.Id,
                        ObsIdB = otherId,
                        Type = "direct",
                        Confidence = 1.0,
                        SuggestedResolution = "keep_both"
                    });
                }
            }
        }

        return results.Take(limit).ToList();
    }

    /// <summary>
    /// Detect temporal supersedence: for each topic_key with multiple active observations,
    /// the head (most recent) contradicts an older one if keyword overlap &lt; 0.4.
    /// Confidence = 0.7. If confidence &gt; 0.8 and autoMark=true, older is marked deprecated.
    /// </summary>
    public async Task<List<ContradictionResult>> DetectTemporalSupersedenceAsync(
        string project, double confidenceThreshold = 0.5, int limit = 50, bool autoMark = false)
    {
        var observations = await _store.RecentObservationsAsync(project, scope: null, limit: 1000);
        var activeObs = observations.Where(o => o.Status == "active").ToList();

        // Group by topic_key, sorted DESC by CreatedAt
        var groups = TopicKeyGrouper.GroupByTopicKey(activeObs);
        var results = new List<ContradictionResult>();

        foreach (var (topicKey, obsList) in groups)
        {
            if (obsList.Count < 2) continue;

            var head = obsList.First(); // most recent (DESC order)
            foreach (var older in obsList.Skip(1))
            {
                var overlap = ComputeKeywordOverlap(head.Content, older.Content);
                if (overlap < 0.4) // contradictory keywords
                {
                    var confidence = 0.7;
                    if (confidence >= confidenceThreshold)
                    {
                        var result = new ContradictionResult
                        {
                            ObsIdA = head.Id,
                            ObsIdB = older.Id,
                            Type = "temporal",
                            Confidence = confidence,
                            SuggestedResolution = "mark_superseded"
                        };
                        results.Add(result);

                        // Auto-mark if confidence > 0.8
                        if (autoMark && confidence > 0.8)
                        {
                            await _store.UpdateObservationAsync(older.Id,
                                new UpdateObservationParams { Status = "deprecated" });
                            await _memRelRepo.SaveRelationAsync(
                                project, head.Id,
                                new MemoryRelation { Type = "supersedes", TargetObservationId = older.Id },
                                _defaultSessionId);
                        }
                    }
                }
                // overlap > 0.7 = reinforcing, not a contradiction → skip
            }
        }

        return results.Take(limit).ToList();
    }

    /// <summary>
    /// Detect embedding conflicts: observations with high TF-IDF cosine similarity (&gt;0.7)
    /// but low keyword overlap (&lt;0.5), indicating a real conflict the embedding missed.
    /// Until ENG-418, uses text-similarity heuristic on Content field.
    /// Confidence = 0.5 + (similarity * 0.4), range [0.5, 0.9].
    /// </summary>
    public async Task<List<ContradictionResult>> DetectEmbeddingConflictsAsync(
        string project, double confidenceThreshold = 0.5, int limit = 50)
    {
        var observations = await _store.RecentObservationsAsync(project, scope: null, limit: 500);
        var activeObs = observations.Where(o => o.Status == "active").ToList();

        if (activeObs.Count < 2) return [];

        // Build TF-IDF vectors in-memory (O(n^2) pairwise, acceptable at P3 scale)
        var contents = activeObs.Select(o => o.Content).ToList();
        var tfidf = BuildTfIdfVectors(contents);
        var results = new List<ContradictionResult>();

        for (int i = 0; i < activeObs.Count; i++)
        {
            for (int j = i + 1; j < activeObs.Count; j++)
            {
                var sim = ComputeCosineSimilarity(tfidf[i], tfidf[j]);
                if (sim <= 0.7) continue;

                var overlap = ComputeKeywordOverlap(activeObs[i].Content, activeObs[j].Content);
                if (overlap >= 0.5) continue; // high overlap = reinforcing, not conflicting

                var confidence = 0.5 + (sim * 0.4); // range [0.5, 0.9]
                if (confidence >= confidenceThreshold)
                {
                    results.Add(new ContradictionResult
                    {
                        ObsIdA = activeObs[i].Id,
                        ObsIdB = activeObs[j].Id,
                        Type = "embedding",
                        Confidence = Math.Round(confidence, 2),
                        SuggestedResolution = "keep_both"
                    });
                }
            }
        }

        return results.Take(limit).ToList();
    }

    // ─── Text-similarity helpers (ENG-418 placeholder) ─────────────────────

    /// <summary>
    /// Jaccard keyword overlap: |keywords_A ∩ keywords_B| / |keywords_A ∪ keywords_B|.
    /// Simple tokenization: lowercase, strip punctuation, split on whitespace.
    /// </summary>
    private static double ComputeKeywordOverlap(string contentA, string contentB)
    {
        var tokensA = Tokenize(contentA);
        var tokensB = Tokenize(contentB);
        var setA = new HashSet<string>(tokensA);
        var setB = new HashSet<string>(tokensB);

        var intersection = setA.Intersect(setB).Count();
        var union = setA.Union(setB).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static string[] Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2) // filter stopwords-like short tokens
            .ToArray();
    }

    /// <summary>
    /// Build TF-IDF vectors for a list of documents.
    /// Returns list of dictionaries: term → TF-IDF score.
    /// </summary>
    private static List<Dictionary<string, double>> BuildTfIdfVectors(List<string> documents)
    {
        var vocab = documents
            .SelectMany(d => Tokenize(d))
            .Distinct()
            .ToList();

        var N = documents.Count;
        var dfs = new Dictionary<string, int>(); // document frequencies

        foreach (var term in vocab)
        {
            dfs[term] = documents.Count(d => Tokenize(d).Contains(term));
        }

        var vectors = new List<Dictionary<string, double>>();
        foreach (var doc in documents)
        {
            var tokens = Tokenize(doc);
            var tf = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            var vec = new Dictionary<string, double>();

            foreach (var term in vocab)
            {
                var tfScore = tf.TryGetValue(term, out var tfVal) ? tfVal : 0;
                var df = dfs[term];
                var idf = Math.Log((N / (double)(df + 1))); // +1 to avoid log(0)
                vec[term] = tfScore * idf;
            }
            vectors.Add(vec);
        }

        return vectors;
    }

    /// <summary>
    /// Cosine similarity between two TF-IDF vectors.
    /// </summary>
    private static double ComputeCosineSimilarity(Dictionary<string, double> vecA, Dictionary<string, double> vecB)
    {
        var keys = vecA.Keys.Intersect(vecB.Keys).ToList();
        if (keys.Count == 0) return 0.0;

        double dotProduct = 0, normA = 0, normB = 0;
        foreach (var k in keys)
        {
            dotProduct += vecA[k] * vecB[k];
        }
        foreach (var v in vecA.Values) normA += v * v;
        foreach (var v in vecB.Values) normB += v * v;

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0.0 : dotProduct / denom;
    }

    public void Dispose()
    {
        // TF-IDF vectors are in-memory; nothing to dispose beyond potential future cleanup
    }
}
