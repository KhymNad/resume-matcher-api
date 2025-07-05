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

            string resumeText;
            using (var stream = file.OpenReadStream())
            {
                resumeText = _extractor.ExtractText(file.FileName, stream);
            }

            resumeText = PreprocessExtractedText(resumeText);


            var nerJson = await _huggingFace.AnalyzeResumeText(resumeText);

            var entities = JsonConvert.DeserializeObject<List<HuggingFaceEntity>>(nerJson) ?? new List<HuggingFaceEntity>();

            // **Updated grouping logic using category mapping**
            var groupedEntities = entities
                .Where(e => !string.IsNullOrEmpty(e.Entity))
                .GroupBy(e => MapToCategory(SimplifyEntityLabel(e.Entity)))  // <-- Use MapToCategory here
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

        private string PreprocessExtractedText(string text)
        {
            // 1. Add space between lowercase and uppercase letter runs (camel case)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=[a-z])(?=[A-Z])", " ");

            // 2. Add space after punctuation if missing (e.g., between words and punctuation)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"([.,:;!?])(?=\S)", "$1 ");

            // 3. Normalize whitespace (replace multiple spaces/newlines with single space)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

            // 4. Trim extra spaces from start/end
            return text.Trim();
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
