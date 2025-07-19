using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Http;

namespace ResumeMatcherAPI.Services
{
    public class FileTextExtractor
    {
        private readonly string _pythonServiceBaseUrl = "http://localhost:5001";

        public async Task<string> ExtractTextAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();

            if (ext == ".pdf")
            {
                return await ExtractTextViaPythonAsync(file);
            }
            else if (ext == ".docx")
            {
                using var stream = file.OpenReadStream();
                return ExtractTextFromDocx(stream);
            }
            else if (ext == ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                return await reader.ReadToEndAsync();
            }
            else
            {
                throw new NotSupportedException("Unsupported file type.");
            }
        }

        private async Task<string> ExtractTextViaPythonAsync(IFormFile file)
        {
            using var httpClient = new HttpClient();

            // Wait for the microservice to be ready (with retries)
            await WaitForServiceReadyAsync(httpClient);

            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream();
            content.Add(new StreamContent(stream), "file", file.FileName);

            var response = await httpClient.PostAsync($"{_pythonServiceBaseUrl}/extract-resume", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("cleaned_text").GetString() ?? string.Empty;
        }

        private async Task WaitForServiceReadyAsync(HttpClient httpClient, int maxAttempts = 5, int delayMs = 1500)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await httpClient.GetAsync($"{_pythonServiceBaseUrl}/healthz");

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

                await Task.Delay(delayMs);
            }

            throw new Exception("Microservice did not become ready after multiple attempts.");
        }

        private string ExtractTextFromDocx(Stream stream)
        {
            using var wordDoc = WordprocessingDocument.Open(stream, false);
            var mainPart = wordDoc.MainDocumentPart;
            var document = mainPart?.Document;
            var body = document?.Body;
            return body?.InnerText ?? string.Empty;
        }
    }
}
