using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ResumeMatcherAPI.Services;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Services;

/// <summary>
/// Integration tests for AdzunaJobService.
/// Tests job search functionality, API integration, and error handling.
/// </summary>
public class AdzunaJobServiceIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AdzunaJobServiceIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SearchJobsAsync_WithSkillsAndLocations_MakesCorrectApiCalls()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        var skills = new List<string> { "Python", "JavaScript" };
        var locations = new List<string> { "New York" };

        // Act
        await service.SearchJobsAsync(skills, locations);

        // Assert
        var requests = _factory.MockAdzunaHandler.ReceivedRequests;
        requests.Should().HaveCount(2); // 2 skills × 1 location

        requests.Should().Contain(r => r.RequestUri!.Query.Contains("what=Python"));
        requests.Should().Contain(r => r.RequestUri!.Query.Contains("what=JavaScript"));
    }

    [Fact]
    public async Task SearchJobsAsync_IncludesApiCredentialsInUrl()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        // Act
        await service.SearchJobsAsync(new List<string> { "Python" }, new List<string> { "NYC" });

        // Assert
        var request = _factory.MockAdzunaHandler.ReceivedRequests.FirstOrDefault();
        request?.RequestUri?.Query.Should().Contain("app_id=test-app-id");
        request?.RequestUri?.Query.Should().Contain("app_key=test-app-key");
    }

    [Fact]
    public async Task SearchJobsAsync_DefaultsToUsCountry()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        // Act
        await service.SearchJobsAsync(new List<string> { "Python" }, new List<string> { "NYC" });

        // Assert
        var request = _factory.MockAdzunaHandler.ReceivedRequests.FirstOrDefault();
        request?.RequestUri?.ToString().Should().Contain("/us/");
    }

    [Fact]
    public async Task SearchJobsAsync_WithCountryOverride_UsesSpecifiedCountry()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        // Act
        await service.SearchJobsAsync(
            new List<string> { "Python" },
            new List<string> { "Toronto" },
            countryOverride: "ca"
        );

        // Assert
        var request = _factory.MockAdzunaHandler.ReceivedRequests.FirstOrDefault();
        request?.RequestUri?.ToString().Should().Contain("/ca/");
    }

    [Fact]
    public async Task SearchJobsAsync_DeduplicatesJobsByRedirectUrl()
    {
        // Arrange
        var service = GetService();
        var duplicateResponse = new
        {
            results = new[]
            {
                new { title = "Python Dev", redirect_url = "https://example.com/job1", company = new { display_name = "Company" }, location = new { display_name = "NYC" }, description = "desc" },
                new { title = "Python Dev 2", redirect_url = "https://example.com/job1", company = new { display_name = "Company" }, location = new { display_name = "NYC" }, description = "desc" } // Same URL
            }
        };

        _factory.MockAdzunaHandler.SetupResponseFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(duplicateResponse), System.Text.Encoding.UTF8, "application/json")
            });

        // Act
        var results = await service.SearchJobsAsync(new List<string> { "Python" }, new List<string> { "NYC" });

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchJobsAsync_LimitsSkillsToTopFive()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        var manySkills = new List<string> { "Python", "JavaScript", "Java", "C#", "Go", "Rust", "Ruby" };

        // Act
        await service.SearchJobsAsync(manySkills, new List<string> { "NYC" });

        // Assert
        var requests = _factory.MockAdzunaHandler.ReceivedRequests;
        requests.Should().HaveCount(5); // Limited to 5 skills
    }

    [Fact]
    public async Task SearchJobsAsync_HandlesEmptySkillsList()
    {
        // Arrange
        var service = GetService();

        // Act
        var results = await service.SearchJobsAsync(new List<string>(), new List<string> { "NYC" });

        // Assert
        results.Should().BeEmpty();
        _factory.MockAdzunaHandler.ReceivedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchJobsAsync_HandlesEmptyLocationsList()
    {
        // Arrange
        var service = GetService();

        // Act
        var results = await service.SearchJobsAsync(new List<string> { "Python" }, new List<string>());

        // Assert
        results.Should().BeEmpty();
        _factory.MockAdzunaHandler.ReceivedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchJobsAsync_ContinuesOnApiError()
    {
        // Arrange
        var service = GetService();
        var callCount = 0;

        _factory.MockAdzunaHandler.SetupResponseFactory(_ =>
        {
            callCount++;
            // First call fails, second succeeds
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { results = new[] { new { title = "Job", redirect_url = "https://example.com/job", company = new { display_name = "C" }, location = new { display_name = "L" }, description = "d" } } }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        // Act
        var results = await service.SearchJobsAsync(
            new List<string> { "Python", "JavaScript" },
            new List<string> { "NYC" });

        // Assert
        results.Should().HaveCount(1); // Got result from second call despite first failing
    }

    [Fact]
    public async Task SearchJobsAsync_FiltersOutNullOrEmptySkillsAndLocations()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        var skills = new List<string> { "Python", "", null!, "   ", "JavaScript" };
        var locations = new List<string> { "", "NYC", null!, "   " };

        // Act
        await service.SearchJobsAsync(skills, locations);

        // Assert
        var requests = _factory.MockAdzunaHandler.ReceivedRequests;
        requests.Should().HaveCount(2); // Only Python and JavaScript with NYC
    }

    [Fact]
    public async Task SearchJobsAsync_UrlEncodesSkillsAndLocations()
    {
        // Arrange
        var service = GetService();
        SetupDefaultAdzunaResponse();

        // Act
        await service.SearchJobsAsync(
            new List<string> { "C++" },
            new List<string> { "New York, NY" });

        // Assert
        var request = _factory.MockAdzunaHandler.ReceivedRequests.FirstOrDefault();
        request?.RequestUri?.Query.Should().Contain("what=C%2B%2B");
        request?.RequestUri?.Query.Should().Contain("where=New%20York");
    }

    [Fact]
    public async Task SearchJobsAsync_ParsesJobListingsCorrectly()
    {
        // Arrange
        var service = GetService();
        var response = new
        {
            results = new[]
            {
                new
                {
                    title = "Senior Python Developer",
                    company = new { display_name = "Tech Corp" },
                    location = new { display_name = "New York, NY" },
                    description = "We are looking for a Python developer",
                    redirect_url = "https://jobs.example.com/123"
                }
            }
        };
        _factory.MockAdzunaHandler.SetupJsonResponse(response);

        // Act
        var results = await service.SearchJobsAsync(
            new List<string> { "Python" },
            new List<string> { "NYC" });

        // Assert
        results.Should().HaveCount(1);
        var job = results.First();
        job.Title.Should().Be("Senior Python Developer");
        job.Company?.DisplayName.Should().Be("Tech Corp");
        job.Location?.DisplayName.Should().Be("New York, NY");
        job.Description.Should().Contain("Python developer");
        job.RedirectUrl.Should().Be("https://jobs.example.com/123");
    }

    [Fact]
    public async Task SearchJobsAsync_HandlesNullResultsInResponse()
    {
        // Arrange
        var service = GetService();
        _factory.MockAdzunaHandler.SetupJsonResponse(new { results = (object[]?)null });

        // Act
        var results = await service.SearchJobsAsync(
            new List<string> { "Python" },
            new List<string> { "NYC" });

        // Assert
        results.Should().BeEmpty();
    }

    private AdzunaJobService GetService()
    {
        _factory.MockAdzunaHandler.Clear();
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdzunaJobService>();
    }

    private void SetupDefaultAdzunaResponse()
    {
        _factory.MockAdzunaHandler.SetupResponseFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        results = new[]
                        {
                            new { title = "Developer", redirect_url = $"https://example.com/job-{Guid.NewGuid()}", company = new { display_name = "Company" }, location = new { display_name = "NYC" }, description = "desc" }
                        }
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
    }
}
