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
        public string? Skill { get; set; }
        public string? Source { get; set; }  // "NER", "substring", "ngram"
        public double? Score { get; set; }  // Removed embedding, keep nullable for flexibility
    }

    public static class SkillMatcher
    {
        private static HashSet<string>? _knownSkills; // Normalized
        private static Dictionary<string, string>? _normalizedSkillMap; // Normalized -> Original

        public static HashSet<string> GetLoadedSkills()
        {
            return _knownSkills ?? new HashSet<string>();
        }

        public static Dictionary<string, string> GetNormalizedSkillMap()
        {
            return _normalizedSkillMap ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Generalized skill validity check.
        /// Accepts skills longer than 2 chars, or short skills only if uppercase or contain # or +.
        /// </summary>
        private static bool IsValidSkill(string skill)
        {
            if (string.IsNullOrWhiteSpace(skill))
                return false;

            skill = skill.Trim();

            if (skill.Length > 2)
                return true;

            // For short skills (1-2 chars), require uppercase letter or '#' or '+'
            if (skill.Any(char.IsUpper) || skill.Contains('#') || skill.Contains('+'))
                return true;

            return false;
        }

        /// <summary>
        /// Normalize a skill by lowercasing, trimming, and removing non-alphanumeric characters except + and #.
        /// </summary>
        private static string NormalizeSkill(string skill)
        {
            string normalized = skill.ToLowerInvariant().Trim();
            normalized = Regex.Replace(normalized, @"[^a-z0-9\s+#]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized;
        }

        /// <summary>
        /// Load skills from the Supabase DB.
        /// </summary>
        public static void LoadSkillsFromDb(string connectionString)
        {
            var skills = new HashSet<string>();
            var normalizedMap = new Dictionary<string, string>();

            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            using var cmd = new NpgsqlCommand("SELECT name FROM skills", conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var originalSkill = reader.GetString(0).Trim();
                var normalizedSkill = NormalizeSkill(originalSkill);

                if (!string.IsNullOrWhiteSpace(normalizedSkill))
                {
                    skills.Add(normalizedSkill);
                    normalizedMap[normalizedSkill] = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(originalSkill.ToLower());
                }
            }

            _knownSkills = skills;
            _normalizedSkillMap = normalizedMap;
        }

        public static List<string> MatchKnownSkills(string text)
        {
            if (_knownSkills == null || _normalizedSkillMap == null)
                return new List<string>();

            string cleanedText = NormalizeSkill(text);
            var found = new HashSet<string>();

            foreach (var normalizedSkill in _knownSkills)
            {
                if (cleanedText.Contains(normalizedSkill) && IsValidSkill(normalizedSkill))
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
        /// Matches skills using NER input, substring/n-gram detection.
        /// </summary>
        public static List<MatchedSkill> MatchSkills(string resumeText, List<string> nerSkills = null)
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

            return matched.OrderBy(m => m.Skill).ToList();
        }
    }
}
