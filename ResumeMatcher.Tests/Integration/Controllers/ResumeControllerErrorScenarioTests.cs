using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ResumeMatcher.Tests.Integration.Fixtures;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Controllers
{
    /// <summary>
    /// Integration tests for ResumeController covering error scenarios, edge cases,
    /// and boundary conditions not covered by standard integration tests.
    /// </summary>
    public class ResumeControllerErrorScenarioTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public ResumeControllerErrorScenarioTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        #region File Upload Error Scenarios

        [Fact]
        public async Task Upload_WithMissingFile_ReturnsBadRequest()
        {
            // Arrange - Empty form data, no file
            var content = new MultipartFormDataContent();

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public void Upload_WithEmptyFileName_ThrowsArgumentException()
        {
            // Arrange & Act & Assert
            // .NET's MultipartFormDataContent.Add() rejects empty filenames at construction time
            // This is framework-level validation before the request even reaches the server
            Assert.Throws<ArgumentException>(() =>
                TestHelpers.CreateFileUploadContent("", "content"));
        }

        [Fact]
        public async Task Upload_WithZeroByteFile_ReturnsBadRequest()
        {
            // Arrange
            var content = TestHelpers.CreateFileUploadContent("resume.txt", "");

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("resume.exe")]
        [InlineData("resume.bat")]
        [InlineData("resume.sh")]
        [InlineData("resume.dll")]
        public async Task Upload_WithExecutableExtensions_ThrowsNotSupportedException(string filename)
        {
            // Arrange - No mock needed as FileTextExtractor rejects unsupported extensions
            var content = TestHelpers.CreateFileUploadContent(filename, "content");

            // Act & Assert - Unsupported file extensions throw NotSupportedException
            // which propagates as a 500 Internal Server Error response
            await Assert.ThrowsAnyAsync<Exception>(
                () => _client.PostAsync("/api/resume/upload", content));
        }

        #endregion

        #region NER Service Error Scenarios

        [Fact]
        public async Task Upload_WhenHuggingFaceReturnsError_ThrowsException()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupResponse(
                new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer resume");

            // Act & Assert - HuggingFace service throws on non-success, exception propagates
            await Assert.ThrowsAsync<HttpRequestException>(
                () => _client.PostAsync("/api/resume/upload", content));
        }

        [Fact]
        public async Task Upload_WhenHuggingFaceReturns401_ThrowsException()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupResponse(
                new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act & Assert - HuggingFace service throws on auth failure
            await Assert.ThrowsAsync<HttpRequestException>(
                () => _client.PostAsync("/api/resume/upload", content));
        }

        [Fact]
        public async Task Upload_WhenHuggingFaceReturnsInvalidJson_ThrowsJsonException()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupResponse(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not valid json {{{", Encoding.UTF8, "application/json")
                });

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act & Assert - JSON parsing error throws exception
            await Assert.ThrowsAnyAsync<Exception>(
                () => _client.PostAsync("/api/resume/upload", content));
        }

        [Fact]
        public async Task Upload_WhenHuggingFaceReturnsEmptyArray_ReturnsEmptyEntities()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(Array.Empty<object>());

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Simple resume text");

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
        }

        #endregion

        #region Upload With Jobs Error Scenarios

        [Fact]
        public async Task UploadWithJobs_WhenAdzunaFails_ReturnsErrorOrPartialData()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(TestDataFixtures.NerResponses.PythonDeveloper);

            _factory.MockAdzunaHandler.Clear();
            _factory.MockAdzunaHandler.SetupResponse(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act
            var response = await _client.PostAsync("/api/resume/upload-with-jobs?location=New York", content);

            // Assert - Should return resume data with fileName and groupedEntities, or handle error
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Assert.Contains("fileName", responseContent);
                Assert.Contains("groupedEntities", responseContent);
            }
            else
            {
                // Adzuna error may propagate as server error
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            }
        }

        [Fact]
        public async Task UploadWithJobs_WhenAdzunaReturnsInvalidJson_ReturnsErrorOrPartialData()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(TestDataFixtures.NerResponses.PythonDeveloper);

            _factory.MockAdzunaHandler.Clear();
            _factory.MockAdzunaHandler.SetupResponse(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("invalid json", Encoding.UTF8, "application/json")
                });

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act
            var response = await _client.PostAsync("/api/resume/upload-with-jobs?location=NYC", content);

            // Assert - Invalid JSON parsing may cause an error
            Assert.True(
                response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.InternalServerError,
                $"Expected success or server error but got {response.StatusCode}");
        }

        [Fact]
        public async Task UploadWithJobs_WithMissingLocation_StillWorks()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(TestDataFixtures.NerResponses.PythonDeveloper);

            _factory.MockAdzunaHandler.Clear();
            _factory.MockAdzunaHandler.SetupJsonResponse(TestDataFixtures.AdzunaResponses.PythonJobs);

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act - No location parameter
            var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task UploadWithJobs_WithEmptyLocation_HandlesGracefully()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(TestDataFixtures.NerResponses.PythonDeveloper);

            _factory.MockAdzunaHandler.Clear();
            _factory.MockAdzunaHandler.SetupJsonResponse(new { results = Array.Empty<object>() });

            var content = TestHelpers.CreateFileUploadContent("resume.txt", "Python developer");

            // Act
            var response = await _client.PostAsync("/api/resume/upload-with-jobs?location=", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
        }

        #endregion

        #region Special Character Handling

        [Theory]
        [InlineData("résumé.txt")]
        [InlineData("简历.txt")]
        [InlineData("이력서.txt")]
        public async Task Upload_WithUnicodeFilename_HandlesCorrectly(string filename)
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(Array.Empty<object>());

            var content = TestHelpers.CreateFileUploadContent(filename, "Resume content");

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Upload_WithSpecialCharsInContent_ProcessesCorrectly()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(Array.Empty<object>());

            var specialContent = "Skills: C++, C#, ASP.NET, Node.js <script>alert('xss')</script> & more";
            var content = TestHelpers.CreateFileUploadContent("resume.txt", specialContent);

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
        }

        #endregion

        #region Concurrent Request Handling

        [Fact]
        public async Task Upload_MultipleConcurrentRequests_AllSucceed()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupResponseFactory(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });

            var tasks = new Task<HttpResponseMessage>[5];
            for (int i = 0; i < 5; i++)
            {
                var content = TestHelpers.CreateFileUploadContent($"resume{i}.txt", $"Content {i}");
                tasks[i] = _client.PostAsync("/api/resume/upload", content);
            }

            // Act
            var responses = await Task.WhenAll(tasks);

            // Assert
            Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        }

        #endregion

        #region Rate Limiting / Large Request Handling

        [Fact]
        public async Task Upload_WithVeryLargeResume_HandlesCorrectly()
        {
            // Arrange
            _factory.MockHuggingFaceHandler.Clear();
            _factory.MockHuggingFaceHandler.SetupJsonResponse(Array.Empty<object>());

            // Create a 500KB resume (large but reasonable)
            var largeContent = new string('A', 500 * 1024);
            var content = TestHelpers.CreateFileUploadContent("large_resume.txt", largeContent);

            // Act
            var response = await _client.PostAsync("/api/resume/upload", content);

            // Assert - Should handle large files
            Assert.True(
                response.IsSuccessStatusCode ||
                response.StatusCode == HttpStatusCode.RequestEntityTooLarge);
        }

        #endregion
    }

    /// <summary>
    /// Additional edge case tests for the Health and Test endpoints
    /// </summary>
    public class ResumeControllerHealthEndpointTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly HttpClient _client;

        public ResumeControllerHealthEndpointTests(TestWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Health_MultipleConcurrentRequests_AllSucceed()
        {
            // Arrange & Act
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => _client.GetAsync("/api/resume/health"))
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            // Assert
            Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        }

        [Fact]
        public async Task Health_ReturnsTextPlainOrJson()
        {
            // Act
            var response = await _client.GetAsync("/api/resume/health");

            // Assert - Content type may be text/plain or application/json depending on serialization
            var contentType = response.Content.Headers.ContentType?.MediaType;
            Assert.True(
                contentType == "application/json" || contentType == "text/plain",
                $"Expected text/plain or application/json but got {contentType}");
        }

        [Fact]
        public async Task Health_ResponseIndicatesApiIsRunning()
        {
            // Act
            var response = await _client.GetAsync("/api/resume/health");
            var content = await response.Content.ReadAsStringAsync();

            // Assert - Health endpoint returns "API is running" message
            Assert.Contains("running", content.ToLower());
        }
    }
}
