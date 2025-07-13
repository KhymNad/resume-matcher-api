using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ResumeMatcherAPI.Helpers
{
    public static class EmbeddingHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string? _embeddingApiUrl;
        private static string? _apiKey;
        public static void Configure(IConfiguration configuration)
        {
            _embeddingApiUrl = configuration["EmbeddingAPI:Url"];
            _apiKey = configuration["EmbeddingAPI:ApiKey"];

            Console.WriteLine($"[EmbeddingHelper] Configured with endpoint: {_embeddingApiUrl}");
        }

        public static List<float>? GetEmbedding(string text)
        {
            if (string.IsNullOrWhiteSpace(_embeddingApiUrl) || string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine("[EmbeddingHelper] ERROR: EmbeddingHelper not configured properly.");
                throw new InvalidOperationException("EmbeddingHelper not configured properly.");
            }

            var payload = new
            {
                inputs = text
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = _httpClient.PostAsync(_embeddingApiUrl, content).Result;
            Console.WriteLine($"Hugging Face HTTP Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
                return null;

            var responseString = response.Content.ReadAsStringAsync().Result;
            Console.WriteLine($"Response Body: {responseString}");

            try
            {
                // Expecting Hugging Face format: [[float, float, ...]]
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<List<List<float>>>(responseString, options);

                return parsed?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }
}
