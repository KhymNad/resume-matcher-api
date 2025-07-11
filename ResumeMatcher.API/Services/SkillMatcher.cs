using System.Globalization;
using Npgsql;

namespace ResumeMatcherAPI.Helpers
{
    /// <summary>
    /// Provides functionality to load known skills from the database 
    /// and match those skills against input text.
    /// </summary>
    public static class SkillMatcher
    {
        // Holds the set of known skills loaded from the database, stored in lowercase for case-insensitive matching.
        private static HashSet<string>? _knownSkills;

        /// <summary>
        /// Loads known skills from the 'skills' table in the database.
        /// Each skill name is converted to lowercase and trimmed for consistent matching.
        /// </summary>
        /// <param name="connectionString">Connection string to the PostgreSQL database.</param>
        public static void LoadSkillsFromDb(string connectionString)
        {
            var skills = new HashSet<string>();

            // Establish a connection to the PostgreSQL database using Npgsql
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Prepare and execute a SQL command to select skill names
            using var cmd = new NpgsqlCommand("SELECT name FROM skills", conn);
            using var reader = cmd.ExecuteReader();

            // Read each skill name from the result set
            while (reader.Read())
            {
                // Get the skill name as a string, trim whitespace, and convert to lowercase
                var skill = reader.GetString(0).Trim().ToLowerInvariant();

                // Add the skill to the hash set if it's not empty
                if (!string.IsNullOrEmpty(skill))
                {
                    skills.Add(skill);
                }
            }

            // Cache the loaded skills in the static field for future matching
            _knownSkills = skills;
        }

        /// <summary>
        /// Matches known skills against the provided text.
        /// Returns a list of skills found within the text.
        /// </summary>
        /// <param name="text">Input text to search for skills.</param>
        /// <returns>List of matched skills in Title Case format.</returns>
        public static List<string> MatchKnownSkills(string text)
        {
            // If the known skills have not been loaded yet, return an empty list
            if (_knownSkills == null) return new List<string>();

            var found = new List<string>();
            var lowerText = text.ToLowerInvariant();

            // Check if each known skill appears anywhere in the input text (case-insensitive)
            foreach (var skill in _knownSkills)
            {
                if (lowerText.Contains(skill))
                {
                    // Convert the skill to Title Case for display (e.g., "csharp" -> "Csharp")
                    found.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(skill));
                }
            }

            // Return distinct skills to avoid duplicates
            return found.Distinct().ToList();
        }
    }
}
