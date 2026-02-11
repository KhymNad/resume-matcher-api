using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for SkillsController.
/// Tests database integration and API response formatting.
/// </summary>
public class SkillsControllerIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SkillsControllerIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllSkills_WithSeededData_ReturnsSkillsList()
    {
        // Arrange
        await _factory.SeedDefaultSkillsAsync();

        // Act
        var response = await _client.GetAsync("/api/skills");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var skills = JsonDocument.Parse(content).RootElement;

        skills.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAllSkills_ReturnsCorrectContentType()
    {
        // Arrange
        await _factory.SeedDefaultSkillsAsync();

        // Act
        var response = await _client.GetAsync("/api/skills");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetAllSkills_ReturnsSkillsWithExpectedProperties()
    {
        // Arrange
        await _factory.SeedDatabaseAsync(context =>
        {
            context.Skills.Add(new Skill
            {
                Id = Guid.NewGuid(),
                Name = "TestSkill",
                Type = "TestType",
                Source = "TestSource"
            });
        });

        // Act
        var response = await _client.GetAsync("/api/skills");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var skills = JsonDocument.Parse(content).RootElement;

        var skill = skills.EnumerateArray().FirstOrDefault(s =>
            s.GetProperty("name").GetString() == "TestSkill");

        skill.GetProperty("name").GetString().Should().Be("TestSkill");
        skill.GetProperty("type").GetString().Should().Be("TestType");
        skill.GetProperty("source").GetString().Should().Be("TestSource");
    }

    [Fact]
    public async Task GetAllSkills_WithEmptyDatabase_ReturnsEmptyArray()
    {
        // Arrange - Use a fresh factory with empty database
        await using var freshFactory = new TestWebApplicationFactory();
        var client = freshFactory.CreateClient();
        await freshFactory.SeedDatabaseAsync(); // Create database but don't add skills

        // Act
        var response = await client.GetAsync("/api/skills");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var skills = JsonDocument.Parse(content).RootElement;

        skills.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetAllSkills_ReturnsAllSeededSkills()
    {
        // Arrange
        var expectedSkillNames = new[] { "Python", "JavaScript", "C#", "SQL", "Docker", "AWS", "React", "Node.js", "Machine Learning", "Data Analysis" };
        await _factory.SeedDefaultSkillsAsync();

        // Act
        var response = await _client.GetAsync("/api/skills");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var skills = JsonDocument.Parse(content).RootElement;

        var returnedNames = skills.EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();

        foreach (var expected in expectedSkillNames)
        {
            returnedNames.Should().Contain(expected);
        }
    }

    [Fact]
    public async Task GetAllSkills_PerformanceTest_RespondsWithinThreshold()
    {
        // Arrange
        await _factory.SeedDefaultSkillsAsync();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await _client.GetAsync("/api/skills");
        stopwatch.Stop();

        // Assert - Should respond within 1 second for a simple database query
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task GetAllSkills_WithManySkills_HandlesLargeDataset()
    {
        // Arrange - Seed many skills
        await _factory.SeedDatabaseAsync(context =>
        {
            for (int i = 0; i < 100; i++)
            {
                context.Skills.Add(new Skill
                {
                    Id = Guid.NewGuid(),
                    Name = $"Skill_{i}",
                    Type = $"Type_{i % 5}",
                    Source = "BulkTest"
                });
            }
        });

        // Act
        var response = await _client.GetAsync("/api/skills");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var skills = JsonDocument.Parse(content).RootElement;

        skills.GetArrayLength().Should().BeGreaterOrEqualTo(100);
    }
}
