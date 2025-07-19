using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class PythonResumeParserService
{
    private readonly HttpClient _httpClient;

    public PythonResumeParserService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://resume-parser-oysv.onrender.com");
    }

    public async Task<string> ExtractTextAsync(IFormFile file)
    {
        await WaitForServiceReadyAsync(_httpClient);

        using var content = new MultipartFormDataContent();
        using var fileStream = file.OpenReadStream();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        content.Add(fileContent, "file", file.FileName);

        var response = await _httpClient.PostAsync("/extract-resume", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task WaitForServiceReadyAsync(HttpClient httpClient, int maxAttempts = 10, int baseDelayMs = 1000)
    {
        var rand = new Random();

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await httpClient.GetAsync("/healthz");

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Ping] Microservice ready (attempt {attempt})");
                    return;
                }

                Console.WriteLine($"[Ping] Not ready: {response.StatusCode} (attempt {attempt})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ping] Error: {ex.Message} (attempt {attempt})");
            }

            // Exponential backoff with jitter
            var delay = baseDelayMs * (int)Math.Pow(2, attempt);
            delay += rand.Next(0, 500); // Add jitter
            Console.WriteLine($"[Ping] Waiting {delay}ms before retry...");
            await Task.Delay(delay);
        }

        throw new Exception("Microservice did not become ready after multiple attempts.");
    }
}
