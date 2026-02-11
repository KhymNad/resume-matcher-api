using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Pipeline;

/// <summary>
/// End-to-end pipeline integration tests.
/// Tests the complete resume processing flow from upload to job matching.
/// These tests verify the full integration between all components.
/// </summary>
public class ResumePipelineIntegrationTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ResumePipelineIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedDefaultSkillsAsync();
    }

    public Task DisposeAsync()
    {
        _factory.MockHuggingFaceHandler.Clear();
        _factory.MockAdzunaHandler.Clear();
        return Task.CompletedTask;
    }

    #region Full Pipeline Tests

    [Fact]
    public async Task CompletePipeline_FromUploadToJobs_ProcessesSuccessfully()
    {
        // Arrange - Set up the complete mock environment
        SetupRealisticNerResponse();
        SetupRealisticJobResponse();

        MockFileTextExtractor.DefaultResumeText = @"
            Sarah Johnson
            Senior Software Engineer
            sarah.johnson@email.com | San Francisco, CA

            EXPERIENCE
            Senior Software Engineer | Netflix | 2021 - Present
            - Built microservices using Python and Go
            - Implemented CI/CD pipelines with Docker and Kubernetes
            - Managed databases including PostgreSQL and Redis

            Software Engineer | Airbnb | 2018 - 2021
            - Developed React frontend applications
            - Worked with AWS services including Lambda and DynamoDB

            EDUCATION
            Master of Science in Computer Science
            Stanford University | 2018

            SKILLS
            Python, Go, JavaScript, React, Docker, Kubernetes, AWS, PostgreSQL
        ";

        using var content = CreateMultipartFormData("sarah_johnson_resume.pdf", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ParseResponse(response);

        // Verify resume parsing worked
        result.RootElement.TryGetProperty("groupedEntities", out var entities).Should().BeTrue();

        // Verify jobs were fetched
        result.RootElement.TryGetProperty("jobResults", out var jobs).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_ExtractsSkillsFromMultipleSources()
    {
        // Arrange
        // NER returns some skills
        var nerResponse = new[]
        {
            new { entity_group = "ORG", word = "Python", score = 0.85f, start = 100, end = 106 },
            new { entity_group = "ORG", word = "Docker", score = 0.82f, start = 200, end = 206 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);
        SetupRealisticJobResponse();

        // Resume also contains skills that might be detected via n-gram matching
        MockFileTextExtractor.DefaultResumeText = @"
            Developer with Python experience
            Also proficient in Docker and Kubernetes
            Experience with Machine Learning and Data Analysis
        ";

        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);
        var entities = result.RootElement.GetProperty("groupedEntities");

        // Skills should be present (from NER and/or n-gram matching)
        entities.TryGetProperty("Skills", out var skills).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_HandlesResumeWithNoSkills_Gracefully()
    {
        // Arrange
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new object[] { }); // No entities detected
        SetupRealisticJobResponse();

        MockFileTextExtractor.DefaultResumeText = @"
            John Doe
            General Worker
            john.doe@email.com

            Looking for opportunities in various fields.
            Available for immediate start.
        ";

        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);
        result.RootElement.TryGetProperty("groupedEntities", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_DetectsEducationViaFallback()
    {
        // Arrange
        // NER doesn't detect education
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new[]
        {
            new { entity_group = "PER", word = "Jane", score = 0.9f, start = 0, end = 4 }
        });

        MockFileTextExtractor.DefaultResumeText = @"
            Jane Doe
            Bachelor of Science in Computer Science
            Massachusetts Institute of Technology | 2020

            Currently pursuing Master's degree at MIT
        ";

        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);
        var entities = result.RootElement.GetProperty("groupedEntities");

        // Education should be detected via fallback regex
        entities.TryGetProperty("Education", out var education).Should().BeTrue();
        education.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Pipeline_MergesConsecutiveNerTokens()
    {
        // Arrange - NER returns split tokens for "New York"
        var nerResponse = new[]
        {
            new { entity_group = "LOC", word = "New", score = 0.9f, start = 50, end = 53 },
            new { entity_group = "LOC", word = "York", score = 0.88f, start = 54, end = 58 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);

        MockFileTextExtractor.DefaultResumeText = "Developer based in New York looking for opportunities.";

        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);
        var entities = result.RootElement.GetProperty("groupedEntities");

        if (entities.TryGetProperty("Locations", out var locations))
        {
            var locationList = locations.EnumerateArray()
                .Select(l => l.GetString())
                .ToList();
            // Should contain "New York" merged, not "New" and "York" separately
            locationList.Should().Contain(l => l!.Contains("New") && l.Contains("York"));
        }
    }

    #endregion

    #region Job Matching Pipeline Tests

    [Fact]
    public async Task Pipeline_MatchesJobsBasedOnSkillsAndLocation()
    {
        // Arrange
        var nerResponse = new[]
        {
            new { entity_group = "ORG", word = "Python", score = 0.9f, start = 50, end = 56 },
            new { entity_group = "LOC", word = "Seattle", score = 0.85f, start = 100, end = 107 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);

        var jobResponse = new
        {
            results = new[]
            {
                new
                {
                    title = "Python Developer",
                    company = new { display_name = "Amazon" },
                    location = new { display_name = "Seattle, WA" },
                    description = "Python developer position",
                    redirect_url = "https://jobs.amazon.com/123"
                }
            }
        };
        _factory.MockAdzunaHandler.SetupJsonResponse(jobResponse);

        MockFileTextExtractor.DefaultResumeText = "Python developer based in Seattle";
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);

        result.RootElement.TryGetProperty("jobResults", out var jobs).Should().BeTrue();
        jobs.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Pipeline_LimitsJobSearchToTopSkills()
    {
        // Arrange - Resume with many skills
        var nerResponse = Enumerable.Range(1, 10)
            .Select(i => new { entity_group = "ORG", word = $"Skill{i}", score = 0.9f, start = i * 10, end = i * 10 + 5 })
            .ToArray();
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);

        _factory.MockAdzunaHandler.SetupResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { results = new[] { new { title = "Job", redirect_url = $"https://example.com/{Guid.NewGuid()}", company = new { display_name = "C" }, location = new { display_name = "L" }, description = "D" } } }),
                System.Text.Encoding.UTF8,
                "application/json")
        });

        MockFileTextExtractor.DefaultResumeText = "Developer with many skills";
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert - Adzuna should not be called for all 10 skills
        // The service limits to top 5 skills
        _factory.MockAdzunaHandler.ReceivedRequests.Count.Should().BeLessThanOrEqualTo(5);
    }

    #endregion

    #region Error Recovery Tests

    [Fact]
    public async Task Pipeline_ContinuesWhenNerReturnsPartialResults()
    {
        // Arrange - First chunk succeeds, second fails, third succeeds
        var callCount = 0;
        _factory.MockHuggingFaceHandler.SetupResponseFactory(_ =>
        {
            callCount++;
            if (callCount == 2)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new[] { new { entity_group = "ORG", word = "Python", score = 0.9f, start = 0, end = 6 } }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        // Create large text that will be chunked
        MockFileTextExtractor.DefaultResumeText = string.Join(" ", Enumerable.Repeat("Python developer experience.", 100));
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pipeline_ReturnsResumeDataEvenIfJobSearchFails()
    {
        // Arrange
        SetupRealisticNerResponse();
        _factory.MockAdzunaHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        MockFileTextExtractor.DefaultResumeText = "Python developer in San Francisco";
        using var content = CreateMultipartFormData("resume.txt", "test");

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ParseResponse(response);
        result.RootElement.TryGetProperty("groupedEntities", out _).Should().BeTrue();
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task Pipeline_ProcessesStandardResumeWithinTimeLimit()
    {
        // Arrange
        SetupRealisticNerResponse();
        SetupRealisticJobResponse();

        MockFileTextExtractor.DefaultResumeText = @"
            Standard resume content with typical length
            Skills: Python, JavaScript, SQL
            Location: New York
            Experience: 5 years
        ";

        using var content = CreateMultipartFormData("resume.txt", "test");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.PostAsync("/api/resume/upload-with-jobs", content);
        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // 5 second threshold
    }

    #endregion

    #region Helper Methods

    private void SetupRealisticNerResponse()
    {
        var nerResponse = new[]
        {
            new { entity_group = "PER", word = "Sarah", score = 0.95f, start = 13, end = 18 },
            new { entity_group = "PER", word = "Johnson", score = 0.93f, start = 19, end = 26 },
            new { entity_group = "LOC", word = "San Francisco", score = 0.89f, start = 80, end = 93 },
            new { entity_group = "ORG", word = "Netflix", score = 0.91f, start = 150, end = 157 },
            new { entity_group = "ORG", word = "Python", score = 0.85f, start = 200, end = 206 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(nerResponse);
    }

    private void SetupRealisticJobResponse()
    {
        var jobResponse = new
        {
            results = new[]
            {
                new
                {
                    title = "Senior Software Engineer",
                    company = new { display_name = "Tech Corp" },
                    location = new { display_name = "San Francisco, CA" },
                    description = "Looking for experienced engineer",
                    redirect_url = "https://jobs.example.com/123"
                },
                new
                {
                    title = "Python Developer",
                    company = new { display_name = "StartupXYZ" },
                    location = new { display_name = "Remote" },
                    description = "Python development role",
                    redirect_url = "https://jobs.example.com/456"
                }
            }
        };
        _factory.MockAdzunaHandler.SetupJsonResponse(jobResponse);
    }

    private static MultipartFormDataContent CreateMultipartFormData(string fileName, string content)
    {
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formData.Add(fileContent, "file", fileName);
        return formData;
    }

    private static async Task<JsonDocument> ParseResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    #endregion
}
