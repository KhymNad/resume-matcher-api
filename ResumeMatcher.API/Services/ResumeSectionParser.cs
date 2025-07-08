using System.Text.RegularExpressions;

namespace ResumeMatcherAPI.Services
{
    public class ResumeSectionParser
    {
        // Define common resume section headers
        private readonly string[] _sectionHeaders = new[]
        {
            "Skills", "Technical Skills", "Work Experience", "Professional Experience", "Education", "Projects",
            "Certifications", "Summary", "Volunteer Experience", "Activities", "Languages", "Interests"
        };

        public Dictionary<string, string> SplitIntoSections(string text)
        {
            var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pattern = string.Join("|", _sectionHeaders.Select(Regex.Escape));

            var matches = Regex.Matches(text, @$"(?<=\n|^)\s*({pattern})\s*\n", RegexOptions.IgnoreCase);
            if (matches.Count == 0)
            {
                // Fallback: return full text as "FullResume"
                sections["FullResume"] = text;
                return sections;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = (i < matches.Count - 1) ? matches[i + 1].Index : text.Length;

                var header = matches[i].Groups[1].Value.Trim();
                var sectionContent = text.Substring(start, end - start).Trim();

                sections[header] = sectionContent;
            }

            return sections;
        }
    }
}
