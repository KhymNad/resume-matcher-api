using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ResumeMatcherAPI.Services;
using System.Text.RegularExpressions;
using System.Globalization;
using ResumeMatcherAPI.Helpers;

namespace ResumeMatcherAPI.Controllers
{
    // Marks the class as a Web API controller
    [ApiController]
    // Base route: /api/resume
    [Route("api/[controller]")]
    public class ResumeController : ControllerBase
    {
        private readonly HuggingFaceNlpService _huggingFace; // Service to call Hugging Face API
        private readonly FileTextExtractor _extractor;        // Service to extract text from resume files
        private readonly ResumeSectionParser _sectionParser;  // Service to parse resume sections
        private readonly AdzunaJobService _adzunaJobService; // Service to get job postings

        // Constructor injects both HuggingFaceNlpService and FileTextExtractor
        public ResumeController(HuggingFaceNlpService huggingFace, FileTextExtractor extractor, ResumeSectionParser sectionParser, AdzunaJobService adzunaJobService)
        {
            _huggingFace = huggingFace;
            _extractor = extractor;
            _sectionParser = sectionParser;
            _adzunaJobService = adzunaJobService;
        }
        /// <summary>
        /// Health check endpoint to verify API is running
        /// GET /api/resume/health
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok("API is running");
        }

        /// <summary>
        /// GET /api/resume/test-huggingface
        /// Sends a sample string to Hugging Face to verify API connectivity and response.
        /// </summary>
        [HttpGet("test-huggingface")]
        public async Task<IActionResult> TestHuggingFace()
        {
            string sample = "Jane Smith worked as a Data Scientist at Facebook and used Python and SQL for 5 years.";

            try
            {
                var nerJson = await _huggingFace.AnalyzeResumeText(sample);
                return Content(nerJson, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Hugging Face API call failed: {ex.Message}");
            }
        }

        /// <summary>
        /// POST /api/resume/upload-with-jobs
        /// Uploads a resume, extracts structured data, and fetches relevant job postings.
        /// </summary>
        [HttpPost("upload-with-jobs")]
        public async Task<IActionResult> UploadResumeWithJobs(IFormFile file)
        {
            var uploadResult = await UploadResume(file) as OkObjectResult;

            if (uploadResult == null || uploadResult.Value == null)
                return StatusCode(500, "Resume parsing failed.");

            var parsedResult = JsonConvert.DeserializeObject<ResumeParsedResult>(JsonConvert.SerializeObject(uploadResult.Value));
            var groupedEntities = parsedResult?.GroupedEntities;

            if (parsedResult == null || groupedEntities == null)
                return BadRequest("Unable to extract structured data from resume.");

            // Extract skills and locations
            var skills = groupedEntities.TryGetValue("Skills", out var skillsList) ? skillsList : new List<string>();
            var locations = groupedEntities.TryGetValue("Locations", out var locationList) ? locationList : new List<string>();

            // Use most specific location for display purposes, if needed
            var displayLocation = locations.LastOrDefault() ?? "Canada";

            // Fetch jobs from Adzuna using skills and locations
            var jobResults = await _adzunaJobService.SearchJobsAsync(skills, locations);

            return Ok(new
            {
                parsedResult.FileName,
                groupedEntities,
                jobResults
            });
        }


        /// <summary>
        /// POST /api/resume/upload
        /// Accepts a resume file, extracts text, sends to Hugging Face NER,
        /// groups entities by simplified labels, and returns them.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadResume(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Extract plain text from the uploaded resume file
            string resumeText = await _extractor.ExtractTextAsync(file);

            // Split resume into logical sections (Skills, Experience, Education, etc.)
            var sections = _sectionParser.SplitIntoSections(resumeText);

            // Focus skill extraction on the most relevant parts (Skills and Experience)
            string skillRelevantText = "";
            if (sections.TryGetValue("Skills", out var skillsSection))
                skillRelevantText += skillsSection + "\n";

            if (sections.TryGetValue("Work Experience", out var workSection))
                skillRelevantText += workSection + "\n";

            if (sections.TryGetValue("Technical Skills", out var techSkillsSection))
                skillRelevantText += techSkillsSection + "\n";

            // If no useful section found, fall back to entire resume
            if (string.IsNullOrWhiteSpace(skillRelevantText))
                skillRelevantText = resumeText;

            SkillMatcher.LoadSkills("Data/skills.txt");

            // List to collect entities from all chunks
            var allEntities = new List<HuggingFaceEntity>();

            // Split the resume text into smaller chunks to avoid API payload size limits
            // Adjust maxChunkSize as needed to fit model token limits (e.g., 1000 characters here)
            var chunks = SplitTextIntoChunks(skillRelevantText, 1000);

            // Call Hugging Face NER model for each chunk separately
            foreach (var chunk in chunks)
            {
                var nerJson = await _huggingFace.AnalyzeResumeText(chunk);

                // Deserialize entities detected in this chunk
                var chunkEntities = JsonConvert.DeserializeObject<List<HuggingFaceEntity>>(nerJson) ?? new List<HuggingFaceEntity>();

                // Adjust Start and End offsets of each entity relative to full resume text
                // This is critical so merged entities have global positions, not chunk-local
                int chunkStartIndex = resumeText.IndexOf(chunk, StringComparison.Ordinal);

                foreach (var entity in chunkEntities)
                {
                    entity.Start += chunkStartIndex;
                    entity.End += chunkStartIndex;
                }

                // Add chunk entities to the aggregate list
                allEntities.AddRange(chunkEntities);
            }

            // Sort all entities by their start index for proper sequential processing
            var rawEntities = allEntities.OrderBy(e => e.Start).ToList();

            // Merge consecutive tokens with the same entity group into one entity
            var mergedEntities = new List<HuggingFaceEntity>();
            if (rawEntities.Count > 0)
            {
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
                    if (SimplifyEntityLabel(next.Entity) == SimplifyEntityLabel(current.Entity) &&
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
            }

            // Group entities by simplified category labels and filter by confidence threshold
            var groupedEntities = mergedEntities
                .Where(e => !string.IsNullOrWhiteSpace(e.Entity) && e.Score >= 0.90f)
                .GroupBy(e => MapToCategory(SimplifyEntityLabel(e.Entity)))
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(x => CleanWord(x.Word))
                        .Where(w => w.Length > 1 && !string.IsNullOrWhiteSpace(w))
                        .Distinct()
                        .ToList()
                );

            // Clean up Skills list (remove generic words, normalize casing)
            if (groupedEntities.ContainsKey("Skills"))
            {
                groupedEntities["Skills"] = CleanSkillList(groupedEntities["Skills"]);
            }

            // Fallback detection for missing Education entities using regex
            if (!groupedEntities.ContainsKey("Education") || groupedEntities["Education"].Count == 0)
            {
                var educationFallback = Regex.Matches(resumeText, @"(Bachelor|Master|B\.Sc|M\.Sc|University|College|Diploma)", RegexOptions.IgnoreCase)
                    .Select(m => m.Value)
                    .Distinct()
                    .ToList();

                if (educationFallback.Any())
                    groupedEntities["Education"] = educationFallback;
            }

            // Match known skills directly from resume text
            var matchedSkills = SkillMatcher.MatchKnownSkills(resumeText);

            if (!groupedEntities.ContainsKey("Skills"))
                groupedEntities["Skills"] = matchedSkills;
            else
                groupedEntities["Skills"].AddRange(matchedSkills.Except(groupedEntities["Skills"]));

            // Extract bullet points and match for additional skill hints
            var bulletPoints = ExtractBulletPoints(resumeText);
            var bulletSkills = bulletPoints.SelectMany(SkillMatcher.MatchKnownSkills).Distinct().ToList();

            groupedEntities["Skills"].AddRange(bulletSkills.Except(groupedEntities["Skills"]));


            // Return the extracted text and grouped entities
            return Ok(new
            {
                fileName = file.FileName,
                // extractedText = resumeText,
                groupedEntities
            });
        }

        /// <summary>
        /// Helper method to split large text into smaller chunks for API calls.
        /// Tries to split at whitespace to avoid breaking words.
        /// </summary>
        private IEnumerable<string> SplitTextIntoChunks(string text, int maxChunkSize = 1000)
        {
            if (string.IsNullOrEmpty(text))
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
        private string SimplifyEntityLabel(string label)
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
        private List<string> CleanSkillList(List<string> skills)
        {
            var banned = new[] { "team", "work", "project", "experience", "management" };
            return skills
                .Where(skill => skill.Length > 2 && !banned.Contains(skill.ToLower()))
                .Select(skill => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(skill.ToLower()))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Extracts bullet-pointed lines from the resume text for further processing.
        /// Supports common bullet styles like •, *, -, and Unicode bullets.
        /// </summary>
        /// <param name="text">The raw resume text</param>
        /// <returns>List of bullet point lines</returns>
        private List<string> ExtractBulletPoints(string text)
        {
            var bulletRegex = new Regex(@"(?:•|\*|\-|\u2022)\s+(.*)", RegexOptions.Multiline);
            return bulletRegex.Matches(text)
                .Select(m => m.Groups[1].Value.Trim())
                .Where(b => b.Length > 2)
                .ToList();
        }

        /// <summary>
        /// Cleans a word by removing unwanted characters and token artifacts like "##".
        /// Also strips symbols and punctuation not part of common skill names.
        /// </summary>
        /// <param name="word">The raw word token</param>
        /// <returns>Cleaned version of the word</returns>
        private string CleanWord(string word)
        {
            var cleaned = word.Trim().Replace("##", "").Replace(".", "");
            return Regex.Replace(cleaned, @"[^a-zA-Z0-9\-\+\.#]", ""); // strip weird symbols
        }

        /// <summary>
        /// Helper to map raw entity labels to friendly category names.
        /// Add or update mappings according to model's entity labels.
        /// </summary>
        private string MapToCategory(string entityLabel)
        {
            return entityLabel.ToUpper() switch
            {
                "PER" or "PERSON" => "Persons",
                "ORG" or "ORGANIZATION" => "Organizations",
                "LOC" or "LOCATION" => "Locations",
                "MISC" => "Skills",
                "SKILL" or "SKILLS" or "TECH" or "TECHNOLOGY" => "Skills",
                "JOB" or "ROLE" or "TITLE" or "POSITION" or "OCCUPATION" or "WORK_EXP" or "WORK_EXPERIENCE" => "WorkExperience",
                "EDU" or "EDUCATION" or "SCHOOL" or "DEGREE" => "Education",
                _ => "Other"
            };
        }
    }

    /// <summary>
    /// Represents a single entity detected by Hugging Face NER model.
    /// </summary>
    public class HuggingFaceEntity
    {
        [JsonProperty("entity_group")]
        public string? Entity { get; set; }

        [JsonProperty("word")]
        public string? Word { get; set; }

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("start")]
        public int Start { get; set; }

        [JsonProperty("end")]
        public int End { get; set; }
    }


    public class ResumeParsedResult
    {
        [JsonProperty("fileName")]
        public string? FileName { get; set; }

        [JsonProperty("groupedEntities")]
        public Dictionary<string, List<string>>? GroupedEntities { get; set; }
    }

}
