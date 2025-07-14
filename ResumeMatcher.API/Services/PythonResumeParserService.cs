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
}
