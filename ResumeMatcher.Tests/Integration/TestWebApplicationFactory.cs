using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeMatcherAPI.Services;

namespace ResumeMatcher.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Replaces external dependencies with in-memory/mock implementations.
/// This pattern is what interviewers look for - proper isolation of external services.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public MockHttpMessageHandler MockHuggingFaceHandler { get; } = new();
    public MockHttpMessageHandler MockAdzunaHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove ALL DbContext and EntityFramework related services to avoid provider conflicts
            var descriptorsToRemove = services
                .Where(d => d.ServiceType.FullName?.Contains("DbContext") == true ||
                           d.ServiceType.FullName?.Contains("EntityFramework") == true ||
                           d.ImplementationType?.FullName?.Contains("Npgsql") == true ||
                           d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                           d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database for isolated testing
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid());
            });

            // Replace IFileTextExtractor with mock implementation
            services.RemoveAll<IFileTextExtractor>();
            services.AddSingleton<IFileTextExtractor, MockFileTextExtractor>();

            // Replace HttpClient for HuggingFaceNlpService with mock
            services.RemoveAll<HuggingFaceNlpService>();
            services.AddHttpClient<HuggingFaceNlpService>()
                .ConfigurePrimaryHttpMessageHandler(() => MockHuggingFaceHandler);

            // Replace HttpClient for AdzunaJobService with mock
            services.RemoveAll<AdzunaJobService>();
            services.AddHttpClient<AdzunaJobService>()
                .ConfigurePrimaryHttpMessageHandler(() => MockAdzunaHandler);
            services.AddSingleton<AdzunaJobService>();

            // Replace PythonResumeParserService
            services.RemoveAll<PythonResumeParserService>();
            services.AddHttpClient<PythonResumeParserService>();
        });

        // Add test configuration
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HuggingFace:ApiKey"] = "test-api-key",
                ["Adzuna:AppId"] = "test-app-id",
                ["Adzuna:AppKey"] = "test-app-key",
                ["EmbeddingAPI:Url"] = "https://test-embedding-api.com",
                ["EmbeddingAPI:ApiKey"] = "test-embedding-key",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:Supabase"] = "Host=localhost;Database=test"
            });
        });
    }

    /// <summary>
    /// Seeds the in-memory database with test data.
    /// </summary>
    public async Task SeedDatabaseAsync(Action<ApplicationDbContext>? seedAction = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        if (seedAction != null)
        {
            seedAction(context);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds the database with default test skills.
    /// </summary>
    public async Task SeedDefaultSkillsAsync()
    {
        await SeedDatabaseAsync(context =>
        {
            context.Skills.AddRange(
                new Skill { Id = Guid.NewGuid(), Name = "Python", Type = "Programming", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "JavaScript", Type = "Programming", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "C#", Type = "Programming", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "SQL", Type = "Database", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "Docker", Type = "DevOps", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "AWS", Type = "Cloud", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "React", Type = "Frontend", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "Node.js", Type = "Backend", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "Machine Learning", Type = "AI", Source = "test" },
                new Skill { Id = Guid.NewGuid(), Name = "Data Analysis", Type = "Analytics", Source = "test" }
            );
        });
    }
}

/// <summary>
/// Mock HttpMessageHandler for controlling HTTP responses in tests.
/// Enables verification of outbound HTTP calls and response simulation.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _requests = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

    public IReadOnlyList<HttpRequestMessage> ReceivedRequests => _requests.AsReadOnly();

    public void SetupResponse(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
    }

    public void SetupResponseFactory(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responseFactory = factory;
    }

    public void SetupJsonResponse<T>(T content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(content),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        _responses.Enqueue(response);
    }

    public void Clear()
    {
        _responses.Clear();
        _requests.Clear();
        _responseFactory = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (_responseFactory != null)
        {
            return Task.FromResult(_responseFactory(request));
        }

        if (_responses.Count > 0)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>
/// Mock FileTextExtractor that returns predefined text for testing.
/// </summary>
public class MockFileTextExtractor : IFileTextExtractor
{
    public static string DefaultResumeText { get; set; } = @"
John Smith
Software Engineer
john.smith@email.com | (555) 123-4567 | New York, NY

SUMMARY
Experienced software engineer with 5+ years of experience in Python, JavaScript, and cloud technologies.

EXPERIENCE
Senior Software Engineer | Google | 2020 - Present
- Developed microservices using Python and Docker
- Implemented machine learning models for data analysis
- Led team of 5 engineers

Software Engineer | Microsoft | 2018 - 2020
- Built web applications using React and Node.js
- Managed SQL databases and AWS infrastructure

EDUCATION
Bachelor of Science in Computer Science
Stanford University | 2018

SKILLS
Python, JavaScript, C#, SQL, Docker, AWS, React, Node.js, Machine Learning
";

    public Task<string> ExtractTextAsync(IFormFile file)
    {
        return Task.FromResult(DefaultResumeText);
    }
}
