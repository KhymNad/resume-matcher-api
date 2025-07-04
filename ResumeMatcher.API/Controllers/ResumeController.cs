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
        private readonly HuggingFaceNlpService _huggingFace;
        private readonly FileTextExtractor _extractor;

        // Inject both HuggingFaceNlpService and FileTextExtractor via constructor
        public ResumeController(HuggingFaceNlpService huggingFace, FileTextExtractor extractor)
        {
            _huggingFace = huggingFace;
            _extractor = extractor;
        }

        /// <summary>
        /// Health check endpoint to verify API is running
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok("API is running");
        }

        /// <summary>
        /// POST /api/resume/upload
        /// Accepts a resume file, extracts text based on file type,
        /// sends the text to Hugging Face for Named Entity Recognition (NER),
        /// groups entities by simplified categories, and returns them.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadResume(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Extract resume text based on file extension using FileTextExtractor service
            string resumeText;
            using (var stream = file.OpenReadStream())
            {
                resumeText = _extractor.ExtractText(file.FileName, stream);
            }

            // Send extracted resume text to Hugging Face NER model
            var nerJson = await _huggingFace.AnalyzeResumeText(resumeText);

            // Deserialize the JSON response into a list of HuggingFaceEntity objects
            var entities = JsonConvert.DeserializeObject<List<HuggingFaceEntity>>(nerJson) ?? new List<HuggingFaceEntity>();

            // Group entities by simplified label (e.g., remove "B-", "I-" prefixes)
            var groupedEntities = entities
                .Where(e => !string.IsNullOrEmpty(e.Entity))
                .GroupBy(e => SimplifyEntityLabel(e.Entity))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Word).Distinct().ToList()
                );

            // Return the original file name and grouped entities in response
            return Ok(new
            {
                fileName = file.FileName,
                groupedEntities
            });
        }

        /// <summary>
        /// Helper to simplify entity labels by stripping "B-" and "I-" prefixes
        /// to get clean category names like "ORG", "LOC", "PER", etc.
        /// </summary>
        private string SimplifyEntityLabel(string label)
        {
            if (label.StartsWith("B-") || label.StartsWith("I-"))
                return label.Substring(2);
            return label;
        }
    }

    /// <summary>
    /// Represents a single entity returned from Hugging Face NER model
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
