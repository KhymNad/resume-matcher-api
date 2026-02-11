using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for ResumeController.
/// Tests the complete HTTP pipeline including routing, model binding,
/// and response serialization with mocked external services.
/// </summary>
public class ResumeControllerIntegrationTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ResumeControllerIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedDefaultSkillsAsync();
        SetupDefaultHuggingFaceResponse();
    }

    public Task DisposeAsync()
    {
        _factory.MockHuggingFaceHandler.Clear();
        _factory.MockAdzunaHandler.Clear();
        return Task.CompletedTask;
    }

    #region Health Check Tests

    [Fact]
    public async Task Health_ReturnsOk_WithExpectedMessage()
    {
        // Act
        var response = await _client.GetAsync("/api/resume/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("API is running");
    }

    [Fact]
    public async Task Health_RespondsQuickly_UnderPerformanceThreshold()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await _client.GetAsync("/api/resume/health");
        stopwatch.Stop();

        // Assert - Health check should respond in under 500ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    #endregion

    #region Test Hugging Face Endpoint Tests

    [Fact]
    public async Task TestHuggingFace_WithValidApiKey_ReturnsNerResults()
    {
        // Arrange
        var expectedEntities = new[]
        {
            new { entity_group = "PER", word = "Jane Smith", score = 0.95, start = 0, end = 10 },
            new { entity_group = "ORG", word = "Facebook", score = 0.92, start = 35, end = 43 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(expectedEntities);

        // Act
        var response = await _client.GetAsync("/api/resume/test-huggingface");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task TestHuggingFace_WhenApiReturnsError_Returns500()
    {
        // Arrange
        _factory.MockHuggingFaceHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Act
        var response = await _client.GetAsync("/api/resume/test-huggingface");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TestHuggingFace_VerifiesAuthorizationHeaderIsSent()
    {
        // Arrange
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new[] { new { entity_group = "PER", word = "Test", score = 0.9 } });

        // Act
        await _client.GetAsync("/api/resume/test-huggingface");

        // Assert
        var request = _factory.MockHuggingFaceHandler.ReceivedRequests.FirstOrDefault();
        request.Should().NotBeNull();
        request!.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    #endregion

    #region Resume Upload Tests

    [Fact]
    public async Task Upload_WithValidFile_ReturnsExtractedData()
    {
        // Arrange
        SetupDefaultHuggingFaceResponse();
        using var content = CreateMultipartFormData("test-resume.txt", "This is a test resume with Python skills.");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(jsonResponse);

        result.RootElement.TryGetProperty("fileName", out _).Should().BeTrue();
        result.RootElement.TryGetProperty("extractedText", out _).Should().BeTrue();
        result.RootElement.TryGetProperty("groupedEntities", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WithNoFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithEmptyFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = CreateMultipartFormData("empty.txt", "");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_ExtractsPersonEntities_FromNerResponse()
    {
        // Arrange
        var nerResponse = new[]
        {
            new { entity_group = "PER", word = "John", score = 0.95f, start = 0, end = 4 },
            new { entity_group = "PER", word = "Smith", score = 0.93f, start = 5, end = 10 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);
        using var content = CreateMultipartFormData("resume.txt", "John Smith is a developer");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        jsonResponse.Should().Contain("groupedEntities");
    }

    [Fact]
    public async Task Upload_DetectsEducationKeywords_ViaFallbackRegex()
    {
        // Arrange
        SetupDefaultHuggingFaceResponse();
        MockFileTextExtractor.DefaultResumeText = @"
            John Smith
            Bachelor of Science in Computer Science
            Stanford University 2020
            Skills: Python, Java
        ";
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        // Education should be detected via fallback regex
        jsonResponse.Should().Contain("Education");
    }

    [Fact]
    public async Task Upload_HandlesLargeResume_ByChunking()
    {
        // Arrange - Create resume text larger than chunk size (1000 chars)
        var largeResumeText = string.Join(" ", Enumerable.Repeat("Python JavaScript React Node.js AWS Docker SQL experience.", 50));
        MockFileTextExtractor.DefaultResumeText = largeResumeText;

        // Setup response factory to handle multiple calls
        var callCount = 0;
        _factory.MockHuggingFaceHandler.SetupResponseFactory(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new[] { new { entity_group = "SKILL", word = "Python", score = 0.9f, start = 0, end = 6 } }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        using var content = CreateMultipartFormData("large-resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify multiple chunks were processed
        callCount.Should().BeGreaterThan(1);
    }

    #endregion

    #region Upload With Jobs Tests

    [Fact]
    public async Task UploadWithJobs_WithValidFile_ReturnsResumeDataAndJobs()
    {
        // Arrange
        SetupDefaultHuggingFaceResponse();
        SetupDefaultAdzunaResponse();
        using var content = CreateMultipartFormData("resume.txt", "Python developer in New York");

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(jsonResponse);

        result.RootElement.TryGetProperty("groupedEntities", out _).Should().BeTrue();
        result.RootElement.TryGetProperty("jobResults", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UploadWithJobs_WithCountryCode_PassesToAdzuna()
    {
        // Arrange
        SetupDefaultHuggingFaceResponse();
        SetupDefaultAdzunaResponse();
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        await _client.PostAsync("/api/resume/upload-with-jobs?countryCode=ca", content);

        // Assert
        var requests = _factory.MockAdzunaHandler.ReceivedRequests;
        requests.Should().Contain(r => r.RequestUri!.ToString().Contains("/ca/"));
    }

    [Fact]
    public async Task UploadWithJobs_WhenAdzunaFails_StillReturnsResumeData()
    {
        // Arrange
        SetupDefaultHuggingFaceResponse();
        _factory.MockAdzunaHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var content = CreateMultipartFormData("resume.txt", "Python developer");

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(jsonResponse);
        result.RootElement.TryGetProperty("groupedEntities", out _).Should().BeTrue();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Upload_WhenHuggingFaceTimesOut_ReturnsServerError()
    {
        // Arrange
        _factory.MockHuggingFaceHandler.SetupResponseFactory(_ => throw new TaskCanceledException("Timeout"));
        using var content = CreateMultipartFormData("resume.txt", "test content");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Upload_WhenHuggingFaceReturnsInvalidJson_HandlesGracefully()
    {
        // Arrange
        _factory.MockHuggingFaceHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("invalid json {{{", System.Text.Encoding.UTF8, "application/json")
        });
        using var content = CreateMultipartFormData("resume.txt", "test content");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        // Should handle invalid JSON gracefully (either return error or empty results)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Helper Methods

    private void SetupDefaultHuggingFaceResponse()
    {
        var defaultNerResponse = new[]
        {
            new { entity_group = "PER", word = "John", score = 0.95f, start = 1, end = 5 },
            new { entity_group = "PER", word = "Smith", score = 0.93f, start = 6, end = 11 },
            new { entity_group = "LOC", word = "New York", score = 0.89f, start = 80, end = 88 },
            new { entity_group = "ORG", word = "Google", score = 0.91f, start = 150, end = 156 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(defaultNerResponse);
    }

    private void SetupDefaultAdzunaResponse()
    {
        var adzunaResponse = new
        {
            results = new[]
            {
                new
                {
                    title = "Senior Python Developer",
                    company = new { display_name = "Tech Corp" },
                    location = new { display_name = "New York, NY" },
                    description = "Looking for experienced Python developer",
                    redirect_url = "https://jobs.example.com/123"
                },
                new
                {
                    title = "Software Engineer",
                    company = new { display_name = "StartupXYZ" },
                    location = new { display_name = "San Francisco, CA" },
                    description = "Full stack development role",
                    redirect_url = "https://jobs.example.com/456"
                }
            }
        };
        _factory.MockAdzunaHandler.SetupJsonResponse(adzunaResponse);
    }

    private static MultipartFormDataContent CreateMultipartFormData(string fileName, string content)
    {
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", fileName);
        return formData;
    }

    #endregion
}
