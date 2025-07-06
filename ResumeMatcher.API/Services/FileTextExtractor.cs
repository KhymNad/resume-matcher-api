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
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream();
            content.Add(new StreamContent(stream), "file", file.FileName);

            var response = await httpClient.PostAsync("http://localhost:5001/extract-resume", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("cleaned_text").GetString();
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
