using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace ResumeMatcherAPI.Services
{
    /// <summary>
    /// Service to interact with Hugging Face NLP models via their Inference API.
    /// Specifically uses a Named Entity Recognition (NER) model to extract entities from resume text.
    /// </summary>
    public class HuggingFaceNlpService
    {
        private readonly HttpClient _httpClient; // Used to send HTTP requests to the Hugging Face API
        private readonly string? _apiKey;         // Hugging Face API key, loaded from configuration

        // The specific endpoint of the Hugging Face model
        // private const string Endpoint = "https://router.huggingface.co/hf-inference/models/dslim/bert-base-NER";
        private const string Endpoint = "https://router.huggingface.co/hf-inference/models/Jean-Baptiste/roberta-large-ner-english";

        /// <summary>
        /// Constructor that initializes the service with an injected HttpClient and configuration.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to make requests</param>
        /// <param name="config">App configuration, used to access the API key</param>
        public HuggingFaceNlpService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;

            // Load the Hugging Face API key from appsettings.json or environment variables
            _apiKey = config["HuggingFace:ApiKey"];
        }

        /// <summary>
        /// Sends resume text to the Hugging Face NER model and returns the JSON result as a string.
        /// </summary>
        /// <param name="resumeText">Plain text extracted from the resume file</param>
        /// <returns>Raw JSON string containing the list of detected entities</returns>
        public async Task<string> AnalyzeResumeText(string resumeText)
        {
            // Log the payload being sent
            Console.WriteLine("==== Hugging Face Request Payload ====");
            Console.WriteLine(resumeText);
            Console.WriteLine("======================================");

            // Clear any existing headers and set the Bearer token
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            // Prepare the request body
            var requestBody = new { 
                inputs = resumeText
            };

            // Serialize and set headers
            var content = new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");

            // Send the POST request
            var response = await _httpClient.PostAsync(Endpoint, content);

            // Read the response
            var json = await response.Content.ReadAsStringAsync();

            // Log the response
            Console.WriteLine("==== Hugging Face API Response ====");
            Console.WriteLine(json);
            Console.WriteLine("===================================");

            // Throw if unsuccessful
            response.EnsureSuccessStatusCode();

            return json;
        }
    }
}
