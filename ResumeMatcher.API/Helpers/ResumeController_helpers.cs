using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ResumeMatcherAPI.Controllers;

namespace ResumeMatcherAPI.Helpers
{
    public static class ResumeControllerHelpers
    {
        /// <summary>
        /// Helper method to split large text into smaller chunks for API calls.
        /// Tries to split at whitespace to avoid breaking words.
        /// </summary>
        public static IEnumerable<string> SplitTextIntoChunks(string text, int maxChunkSize = 1000)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            int offset = 0;
            while (offset < text.Length)
            {
                int length = Math.Min(maxChunkSize, text.Length - offset);

                // Try to break on last whitespace inside maxChunkSize
                if (offset + length < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', offset + length);
                    if (lastSpace > offset)
                        length = lastSpace - offset;
                }

                yield return text.Substring(offset, length).Trim();
                offset += length;
            }
        }

        /// <summary>
        /// Helper to simplify entity labels by removing "B-" and "I-" prefixes.
        /// </summary>
        public static string SimplifyEntityLabel(string label)
        {
            if (label.StartsWith("B-") || label.StartsWith("I-"))
                return label.Substring(2);
            return label;
        }

        /// <summary>
        /// Cleans and normalizes a list of skill strings by removing banned words,
        /// formatting casing to title case, and eliminating duplicates.
        /// </summary>
        /// <param name="skills">List of raw skill strings</param>
        /// <returns>Cleaned list of formatted skill names</returns>
        public static List<string> CleanSkillList(List<string> skills)
        {
            var banned = new[] { "team", "work", "project", "experience", "management" };
            return skills
                .Where(skill => skill.Length > 2 && !banned.Contains(skill.ToLower()))
                .Select(skill => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(skill.ToLower()))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Cleans a word by removing unwanted characters and token artifacts like "##".
        /// Also strips symbols and punctuation not part of common skill names.
        /// </summary>
        /// <param name="word">The raw word token</param>
        /// <returns>Cleaned version of the word</returns>
        public static string CleanWord(string word)
        {
            var cleaned = word.Trim().Replace("##", "").Replace(".", "");
            return Regex.Replace(cleaned, @"[^a-zA-Z0-9\-\+\.#]", ""); // strip weird symbols
        }

        /// <summary>
        /// Helper to map raw entity labels to friendly category names.
        /// Add or update mappings according to model's entity labels.
        /// </summary>
        public static string MapToCategory(string entityLabel)
        {
            return entityLabel.ToUpper() switch
            {
                "PER" or "PERSON" => "Persons",
                // "ORG" or "ORGANIZATION" => "Organizations",
                "LOC" or "LOCATION" => "Locations",
                "MISC" => "Skills",
                "ORG" or "ORGANIZATION" or "SKILL" or "SKILLS" or "TECH" or "TECHNOLOGY" => "Skills",
                "JOB" or "ROLE" or "TITLE" or "POSITION" or "OCCUPATION" or "WORK_EXP" or "WORK_EXPERIENCE" => "WorkExperience",
                "EDU" or "EDUCATION" or "SCHOOL" or "DEGREE" => "Education",
                _ => "Other"
            };
        }

        /// <summary>
        /// Adjust Start and End offsets of each entity relative to full resume text.
        /// This is critical so merged entities have global positions, not chunk-local.
        /// </summary>
        public static void AdjustEntityOffsets(List<HuggingFaceEntity> chunkEntities, string fullText, string chunk)
        {
            int chunkStartIndex = fullText.IndexOf(chunk, StringComparison.Ordinal);
            foreach (var entity in chunkEntities)
            {
                entity.Start += chunkStartIndex;
                entity.End += chunkStartIndex;
            }
        }

        /// <summary>
        /// Merge consecutive tokens with the same entity group into one entity.
        /// </summary>
        public static List<HuggingFaceEntity> MergeConsecutiveEntities(List<HuggingFaceEntity> rawEntities)
        {
            var mergedEntities = new List<HuggingFaceEntity>();
            if (rawEntities.Count == 0)
                return mergedEntities;

            var current = new HuggingFaceEntity
            {
                Entity = rawEntities[0].Entity,
                Word = rawEntities[0].Word,
                Start = rawEntities[0].Start,
                End = rawEntities[0].End,
                Score = rawEntities[0].Score
            };

            for (int i = 1; i < rawEntities.Count; i++)
            {
                var next = rawEntities[i];

                // Check if same entity group and tokens are consecutive
                if (SimplifyEntityLabel(next.Entity ?? string.Empty) == SimplifyEntityLabel(current.Entity ?? string.Empty) &&
                    next.Start == current.End)
                {
                    current.Word += next.Word;
                    current.End = next.End;
                    current.Score = Math.Min(current.Score, next.Score); // keep min confidence
                }
                else
                {
                    mergedEntities.Add(current);
                    current = new HuggingFaceEntity
                    {
                        Entity = next.Entity,
                        Word = next.Word,
                        Start = next.Start,
                        End = next.End,
                        Score = next.Score
                    };
                }
            }
            mergedEntities.Add(current);
            return mergedEntities;
        }

        /// <summary>
        /// Group entities by simplified category labels and filter by confidence threshold.
        /// Also cleans skill list if present.
        /// </summary>
        public static Dictionary<string, List<string>> GroupAndCleanEntities(List<HuggingFaceEntity> mergedEntities)
        {
            var groupedEntities = mergedEntities
                .Where(e => !string.IsNullOrWhiteSpace(e.Entity) && e.Score >= 0.70f)
                .GroupBy(e => MapToCategory(SimplifyEntityLabel(e.Entity ?? string.Empty)))
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(x => CleanWord(x.Word ?? string.Empty))
                        .Where(w => w.Length > 1 && !string.IsNullOrWhiteSpace(w))
                        .Distinct()
                        .ToList()
                );

            if (groupedEntities.ContainsKey("Skills"))
            {
                groupedEntities["Skills"] = CleanSkillList(groupedEntities["Skills"]);
            }

            return groupedEntities;
        }

        /// <summary>
        /// Fallback detection for missing Education entities using regex.
        /// Adds results to groupedEntities if missing.
        /// </summary>
        public static void FallbackEducationDetection(string resumeText, Dictionary<string, List<string>> groupedEntities)
        {
            if (!groupedEntities.ContainsKey("Education") || groupedEntities["Education"].Count == 0)
            {
                var educationFallback = Regex.Matches(resumeText, @"(Bachelor|Master|B\.Sc|M\.Sc|University|College|Diploma)", RegexOptions.IgnoreCase)
                    .Select(m => m.Value)
                    .Distinct()
                    .ToList();

                if (educationFallback.Any())
                    groupedEntities["Education"] = educationFallback;
            }
        }
    }
}
