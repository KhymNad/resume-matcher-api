using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Http;

namespace ResumeMatcherAPI.Services
{
    public class FileTextExtractor
    {
        private readonly string _pythonServiceBaseUrl = "https://resume-parser-oysv.onrender.com"; // <-- updated for production

        public async Task<string> ExtractTextAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();

            return ext switch
            {
                ".pdf" => await ExtractTextViaPythonAsync(file),
                ".docx" => ExtractTextFromDocx(file.OpenReadStream()),
                ".txt" => await ReadTextFileAsync(file),
                _ => throw new NotSupportedException("Unsupported file type.")
            };
        }

        // ============= FOR MICROSERVICE DEPLOYED ON RENDER ========================
        // private async Task<string> ExtractTextViaPythonAsync(IFormFile file)
        // {
        //     using var httpClient = new HttpClient();
        //     await WaitForServiceReadyAsync(httpClient); // improved retry logic

        //     using var content = new MultipartFormDataContent();
        //     using var stream = file.OpenReadStream();
        //     content.Add(new StreamContent(stream), "file", file.FileName);

        //     var response = await httpClient.PostAsync($"{_pythonServiceBaseUrl}/extract-resume", content);
        //     response.EnsureSuccessStatusCode();

        //     var json = await response.Content.ReadAsStringAsync();
        //     var doc = JsonDocument.Parse(json);
        //     return doc.RootElement.GetProperty("cleaned_text").GetString() ?? string.Empty;
        // }

        // private async Task WaitForServiceReadyAsync(HttpClient httpClient, int maxAttempts = 10, int baseDelayMs = 1000)
        // {
        //     var rand = new Random();
        //     Console.WriteLine("Pinging Python Microservice from ==========FileTextExtractor.cs==========");

        //     for (int attempt = 1; attempt <= maxAttempts; attempt++)
        //     {
        //         try
        //         {
        //             var response = await httpClient.GetAsync($"{_pythonServiceBaseUrl}/healthz");

        //             if (response.IsSuccessStatusCode)
        //             {
        //                 Console.WriteLine($"[Ping] Microservice ready (attempt {attempt})");
        //                 return;
        //             }

        //             Console.WriteLine($"[Ping] Not ready: {response.StatusCode} (attempt {attempt})");
        //         }
        //         catch (Exception ex)
        //         {
        //             Console.WriteLine($"[Ping] Error: {ex.Message} (attempt {attempt})");
        //         }

        //         int delay = baseDelayMs * (int)Math.Pow(2, attempt); // exponential backoff
        //         delay += rand.Next(0, 500); // add jitter
        //         Console.WriteLine($"[Ping] Waiting {delay}ms before retry...");
        //         await Task.Delay(delay);
        //     }

        //     throw new Exception("Microservice did not become ready after multiple attempts.");
        // }
        // ==========================================================

        private async Task<string> ExtractTextViaPythonAsync(IFormFile file)
        {
            var tempFilePath = Path.GetTempFileName();
            await using (var stream = File.Create(tempFilePath))
            {
                await file.CopyToAsync(stream);
            }

            string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Python", "parse_resume_script.py");
            var startInfo = new ProcessStartInfo
            {
                FileName = "/opt/venv/bin/python",
                Arguments = $"\"{scriptPath}\" \"{tempFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            File.Delete(tempFilePath);

            if (process.ExitCode != 0)
            {
                Console.WriteLine("Python Error: " + error);
                throw new Exception("Python script failed");
            }

            return output.Trim();
        }

        private string ExtractTextFromDocx(Stream stream)
        {
            using var wordDoc = WordprocessingDocument.Open(stream, false);
            var mainPart = wordDoc.MainDocumentPart;
            var document = mainPart?.Document;
            var body = document?.Body;
            return body?.InnerText ?? string.Empty;
        }

        private async Task<string> ReadTextFileAsync(IFormFile file)
        {
            using var reader = new StreamReader(file.OpenReadStream());
            return await reader.ReadToEndAsync();
        }
    }
}
