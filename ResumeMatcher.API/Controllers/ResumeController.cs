using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ResumeMatcherAPI.Services;

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

            string resumeText = await _extractor.ExtractTextAsync(file);

            var nerJson = await _huggingFace.AnalyzeResumeText(resumeText);

            var entities = JsonConvert.DeserializeObject<List<HuggingFaceEntity>>(nerJson) ?? new List<HuggingFaceEntity>();

            var groupedEntities = entities
                .Where(e => !string.IsNullOrEmpty(e.Entity))
                .GroupBy(e => MapToCategory(SimplifyEntityLabel(e.Entity)))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Word).Distinct().ToList()
                );

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
        /// Helper to simplify entity labels by removing "B-" and "I-" prefixes.
        /// </summary>
        private string SimplifyEntityLabel(string label)
        {
            if (label.StartsWith("B-") || label.StartsWith("I-"))
                return label.Substring(2);
            return label;
        }

        /// <summary>
        /// Helper to map raw entity labels to friendly category names.
        /// Add or update mappings according to your model's entity labels.
        /// </summary>
        private string MapToCategory(string entityLabel)
        {
            return entityLabel switch
            {
                "SKILL" or "SKILLS" => "Skills",
                "WORK_EXP" or "WORK_EXPERIENCE" or "EXPERIENCE" => "WorkExperience",
                "EDUCATION" => "Education",
                "ORG" or "ORGANIZATION" => "Organizations",
                "PER" or "PERSON" => "Persons",
                "LOC" or "LOCATION" => "Locations",
                _ => "Other"
            };
        }
    }

    /// <summary>
    /// Represents a single entity detected by Hugging Face NER model.
    /// </summary>
    public class HuggingFaceEntity
    {
        [JsonProperty("entity")]
        public string Entity { get; set; } = string.Empty;

        [JsonProperty("score")]
        public float Score { get; set; }

        [JsonProperty("word")]
        public string Word { get; set; } = string.Empty;

        [JsonProperty("start")]
        public int Start { get; set; }

        [JsonProperty("end")]
        public int End { get; set; }
    }
}
