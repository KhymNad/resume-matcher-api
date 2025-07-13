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
        private readonly AdzunaJobService _adzunaJobService; // Service to get job postings

        //private readonly SkillService _skillService; // Service to get Skills from Supabase DB
        //private readonly string _dbConnectionString;

        // Constructor injects all required services
        public ResumeController(HuggingFaceNlpService huggingFace, FileTextExtractor extractor, AdzunaJobService adzunaJobService, SkillService skillService, IConfiguration configuration)
        {
            _huggingFace = huggingFace;
            _extractor = extractor;
            _adzunaJobService = adzunaJobService;
            //_skillService = skillService;
            //_dbConnectionString = configuration.GetConnectionString("Supabase") ?? throw new InvalidOperationException("Supabase connection string is missing.");

            // Load skills once on controller startup
            // SkillMatcher.LoadSkillsFromDb(_dbConnectionString);
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
        public async Task<IActionResult> UploadResumeWithJobs(IFormFile file, [FromQuery] string? countryCode = null)
        {
            var uploadResult = await UploadResume(file) as OkObjectResult;

            if (uploadResult == null || uploadResult.Value == null)
                return StatusCode(500, "Resume parsing failed.");

            var parsedResult = JsonConvert.DeserializeObject<ResumeParsedResult>(JsonConvert.SerializeObject(uploadResult.Value));
            var groupedEntities = parsedResult?.GroupedEntities;

            if (parsedResult == null || groupedEntities == null)
                return BadRequest("Unable to extract structured data from resume.");

            var skills = groupedEntities.TryGetValue("Skills", out var skillsList) ? skillsList : new List<string>();
            var locations = groupedEntities.TryGetValue("Locations", out var locationList) ? locationList : new List<string>();

            var displayLocation = locations.LastOrDefault() ?? "Canada";

            // Pass the countryCode (can be null) to the service
            var jobResults = await _adzunaJobService.SearchJobsAsync(skills, locations, countryCode);

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
            if (file == null || file.Length == 0)   // Validate that a file was uploaded
                return BadRequest("No file uploaded.");

            string resumeText = await _extractor.ExtractTextAsync(file);    // Extract plain text from the uploaded resume file
            // SkillMatcher.LoadSkillsFromDb(_dbConnectionString);  // Load skills from the database for skill matching

            var allEntities = new List<HuggingFaceEntity>();
            var chunks = ResumeControllerHelpers.SplitTextIntoChunks(resumeText, 1000); // Split resume text into manageable chunks to avoid exceeding API limits

            // Analyze each chunk using Hugging Face NER and collect all entities
            foreach (var chunk in chunks)
            {
                var nerJson = await _huggingFace.AnalyzeResumeText(chunk);
                var chunkEntities = JsonConvert.DeserializeObject<List<HuggingFaceEntity>>(nerJson) ?? new List<HuggingFaceEntity>();

                // Adjust entity start/end positions to match original text offsets
                ResumeControllerHelpers.AdjustEntityOffsets(chunkEntities, resumeText, chunk);
                allEntities.AddRange(chunkEntities);
            }

            // Sort and merge consecutive entity tokens into complete entities
            var rawEntities = allEntities.OrderBy(e => e.Start).ToList();
            var mergedEntities = ResumeControllerHelpers.MergeConsecutiveEntities(rawEntities);
            var groupedEntities = ResumeControllerHelpers.GroupAndCleanEntities(mergedEntities);    // Group entities by type (e.g., Skills, Education) and clean them

            // Use regex-based fallback to detect education if model missed it
            ResumeControllerHelpers.FallbackEducationDetection(resumeText, groupedEntities);

            // Extract skills identified from the NER model
            var nerSkills = groupedEntities.ContainsKey("Skills") ? groupedEntities["Skills"] : new List<string>();
            var matchedSkillObjs = SkillMatcher.MatchSkills(resumeText, nerSkills); // Match NER + fuzzy skills against database skill list

            groupedEntities["Skills"] = matchedSkillObjs
                .Select(s => s.Skill)
                .Distinct()
                .OrderBy(s => s)
                .ToList()!;

            var detailedSkills = matchedSkillObjs
                .Select(s => new
                {
                    s.Skill,
                    s.Source,
                    Score = (string?)null
                });

            return Ok(new
            {
                fileName = file.FileName,
                extractedText = resumeText,
                mergedEntities = mergedEntities,
                groupedEntities,
                detailedSkills
            });
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
