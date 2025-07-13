using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace ResumeMatcherAPI.Helpers
{
    public class MatchedSkill
    {
        public string Skill { get; set; }
        public string Source { get; set; }  // "NER", "substring", "ngram", "embedding"
        public double? Score { get; set; }  // Optional: confidence from embeddings
    }

    public static class SkillMatcher
    {
        private static HashSet<string>? _knownSkills; // Normalized
        private static Dictionary<string, string>? _normalizedSkillMap; // Normalized -> Original
        private static Dictionary<string, List<float>> _skillEmbeddings = new(); // Normalized -> Embedding vector

        public static HashSet<string> GetLoadedSkills()
        {
            return _knownSkills ?? new HashSet<string>();
        }

        public static Dictionary<string, string> GetNormalizedSkillMap()
        {
            return _normalizedSkillMap ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Normalize a skill by lowercasing, trimming, and removing non-alphanumeric characters.
        /// </summary>
        private static string NormalizeSkill(string skill)
        {
            string normalized = skill.ToLowerInvariant().Trim();
            // Allow + and # for C++, C#, etc.
            normalized = Regex.Replace(normalized, @"[^a-z0-9\s+#]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized;
        }

        /// <summary>
        /// Load skills and embeddings from the Supabase DB.
        /// </summary>
        public static void LoadSkillsFromDb(string connectionString)
        {
            var skills = new HashSet<string>();
            var normalizedMap = new Dictionary<string, string>();
            var embeddings = new Dictionary<string, List<float>>();

            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand("SELECT name, embedding_array FROM skills", conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var originalSkill = reader.GetString(0).Trim();
                var normalizedSkill = NormalizeSkill(originalSkill);

                if (!string.IsNullOrWhiteSpace(normalizedSkill))
                {
                    skills.Add(normalizedSkill);
                    normalizedMap[normalizedSkill] = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(originalSkill.ToLower());

                    if (!reader.IsDBNull(1))
                    {
                        // Read the vector as float[] directly (requires pgvector .NET support)
                        var embeddingVector = reader.GetFieldValue<float[]>(1);
                        if (embeddingVector != null && embeddingVector.Length > 0)
                        {
                            embeddings[normalizedSkill] = embeddingVector.ToList();
                        }
                    }
                }
            }

            _knownSkills = skills;
            _normalizedSkillMap = normalizedMap;
            _skillEmbeddings = embeddings;
        }

        public static List<string> MatchKnownSkills(string text)
        {
            if (_knownSkills == null || _normalizedSkillMap == null)
                return new List<string>();

            string cleanedText = NormalizeSkill(text);
            var found = new HashSet<string>();

            foreach (var normalizedSkill in _knownSkills)
            {
                if (cleanedText.Contains(normalizedSkill))
                {
                    found.Add(_normalizedSkillMap[normalizedSkill]);
                }
            }

            return found.OrderBy(x => x).ToList();
        }

        public static List<string> MatchSkillsWithNGrams(string text, int maxGramSize = 5)
        {
            if (_knownSkills == null || _normalizedSkillMap == null)
                return new List<string>();

            var tokens = NormalizeSkill(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = new HashSet<string>();

            for (int n = 1; n <= maxGramSize; n++)
            {
                for (int i = 0; i <= tokens.Length - n; i++)
                {
                    var ngram = string.Join(" ", tokens.Skip(i).Take(n));
                    if (_knownSkills.Contains(ngram))
                    {
                        found.Add(_normalizedSkillMap[ngram]);
                    }
                }
            }

            return found.OrderBy(x => x).ToList();
        }

        public static List<string> MatchSkillsSmart(string text)
        {
            var set = new HashSet<string>();
            set.UnionWith(MatchKnownSkills(text));
            set.UnionWith(MatchSkillsWithNGrams(text));
            return set.OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Computes cosine similarity between two float vectors.
        /// </summary>
        private static double CosineSimilarity(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return 0;

            double dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Count; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            return (magA == 0 || magB == 0) ? 0 : dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }

        /// <summary>
        /// Matches skills using NER input, substring/n-gram detection, and embeddings.
        /// </summary>
        public static List<MatchedSkill> MatchSkills(string resumeText, List<string> nerSkills = null, float embeddingThreshold = 0.75f)
        {
            var matched = new List<MatchedSkill>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            nerSkills ??= new List<string>();

            // 1. Add NER-tagged skills
            foreach (var skill in nerSkills)
            {
                var cleaned = skill.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) && seen.Add(cleaned))
                {
                    matched.Add(new MatchedSkill
                    {
                        Skill = cleaned,
                        Source = "NER"
                    });
                }
            }

            // 2. Substring + N-gram matches
            var smartMatches = MatchSkillsSmart(resumeText);
            foreach (var skill in smartMatches)
            {
                if (seen.Add(skill))
                {
                    matched.Add(new MatchedSkill
                    {
                        Skill = skill,
                        Source = "ngram"
                    });
                }
            }

            // 3. Embedding match (if we have known skill vectors)
            var resumeEmbedding = EmbeddingHelper.GetEmbedding(resumeText);
            if (resumeEmbedding != null)
            {
                foreach (var kvp in _skillEmbeddings)
                {
                    string normalizedSkill = kvp.Key;
                    List<float> skillEmbedding = kvp.Value;

                    double similarity = CosineSimilarity(resumeEmbedding, skillEmbedding);
                    if (similarity >= embeddingThreshold)
                    {
                        var originalSkill = _normalizedSkillMap.ContainsKey(normalizedSkill)
                            ? _normalizedSkillMap[normalizedSkill]
                            : normalizedSkill;

                        if (seen.Add(originalSkill))
                        {
                            matched.Add(new MatchedSkill
                            {
                                Skill = originalSkill,
                                Source = "embedding",
                                Score = similarity
                            });
                        }
                    }
                }
            }

            return matched.OrderByDescending(m => m.Score ?? 1.0).ToList();
        }
    }
}
