using Microsoft.AspNetCore.Http;

namespace ResumeMatcherAPI.Services
{
    public interface IFileTextExtractor
    {
        Task<string> ExtractTextAsync(IFormFile file);
    }
}
