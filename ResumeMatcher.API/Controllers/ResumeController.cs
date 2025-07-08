using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ResumeMatcherAPI.Services;
using System.Text.RegularExpressions;

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

        // Constructor injects both HuggingFaceNlpService and FileTextExtractor
        public ResumeController(HuggingFaceNlpService huggingFace, FileTextExtractor extractor)
        {
            _huggingFace = huggingFace;
            _extractor = extractor;
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

            // List to collect entities from all chunks
            var allEntities = new List<HuggingFaceEntity>();

            // Split the resume text into smaller chunks to avoid API payload size limits
            // Adjust maxChunkSize as needed to fit model token limits (e.g., 1000 characters here)
            var chunks = SplitTextIntoChunks(resumeText, 1000);

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

            // Return the extracted text and grouped entities
            return Ok(new
            {
                fileName = file.FileName,
                extractedText = resumeText,
                groupedEntities
            });
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
                "MISC" => "Skills", // can rename to "Skills" or "Technologies"
                "SKILL" or "SKILLS" => "Skills",
                "EXPERIENCE" or "WORK_EXP" or "WORK_EXPERIENCE" => "WorkExperience",
                "EDUCATION" => "Education",
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
        public string Entity { get; set; }

        [JsonProperty("word")]
        public string Word { get; set; }

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("start")]
        public int Start { get; set; }

        [JsonProperty("end")]
        public int End { get; set; }
    }
}
