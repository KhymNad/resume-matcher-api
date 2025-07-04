using System.IO;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace ResumeMatcherAPI.Services
{
    public class FileTextExtractor
    {
        public string ExtractText(string fileName, Stream fileStream)
        {
            var ext = Path.GetExtension(fileName).ToLower();

            if (ext == ".pdf")
            {
                return ExtractTextFromPdf(fileStream);
            }
            else if (ext == ".docx")
            {
                return ExtractTextFromDocx(fileStream);
            }
            else if (ext == ".txt")
            {
                using var reader = new StreamReader(fileStream);
                return reader.ReadToEnd();
            }
            else
            {
                throw new NotSupportedException("Unsupported file type.");
            }
        }

        private string ExtractTextFromPdf(Stream stream)
        {
            using var pdf = PdfDocument.Open(stream);
            var textBuilder = new System.Text.StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                textBuilder.AppendLine(page.Text);
            }
            return textBuilder.ToString();
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
