using System.Globalization;

namespace ResumeMatcherAPI.Helpers
{
    public static class SkillMatcher
    {
        private static HashSet<string>? _knownSkills;

        public static void LoadSkills(string filePath)
        {
            if (File.Exists(filePath))
            {
                _knownSkills = File.ReadAllLines(filePath)
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Where(s => s.Length > 1)
                    .ToHashSet();
            }
            else
            {
                _knownSkills = new HashSet<string>();
            }
        }

        public static List<string> MatchKnownSkills(string text)
        {
            if (_knownSkills == null) return new List<string>();

            var found = new List<string>();
            foreach (var skill in _knownSkills)
            {
                if (text.ToLowerInvariant().Contains(skill))
                    found.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(skill));
            }

            return found.Distinct().ToList();
        }
    }
}
