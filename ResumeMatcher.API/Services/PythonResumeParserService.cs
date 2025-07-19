using System.Net.Http.Headers;
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
        await WaitForServiceReadyAsync();

        using var content = new MultipartFormDataContent();
        using var fileStream = file.OpenReadStream();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        content.Add(fileContent, "file", file.FileName);

        var response = await _httpClient.PostAsync("/extract-resume", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        return result;
    }

    private async Task WaitForServiceReadyAsync(int maxAttempts = 5, int delayMs = 1500)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync("/healthz");

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Ping] Microservice ready (attempt {attempt})");
                    return;
                }

                Console.WriteLine($"[Ping] Not ready: {response.StatusCode} (attempt {attempt})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ping] Error contacting microservice: {ex.Message} (attempt {attempt})");
            }

            await Task.Delay(delayMs);
        }

        throw new Exception("Microservice did not become ready after multiple attempts.");
    }
}
