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
        // Ping the service first to wake it up
        await PingMicroserviceAsync();

        // Wait briefly to allow cold start to finish
        await Task.Delay(2000);

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

    private async Task PingMicroserviceAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/healthz");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Ping] Microservice unhealthy: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ping] Microservice unreachable: {ex.Message}");
        }
    }
}
