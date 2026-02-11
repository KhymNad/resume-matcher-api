using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResumeMatcherAPI.Services;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Services;

/// <summary>
/// Integration tests for HuggingFaceNlpService.
/// Tests HTTP client behavior, request formatting, and response handling.
/// </summary>
public class HuggingFaceServiceIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public HuggingFaceServiceIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnalyzeResumeText_SendsCorrectPayloadFormat()
    {
        // Arrange
        var service = GetService();
        var testText = "John Smith is a Python developer";
        string? capturedPayload = null;

        _factory.MockHuggingFaceHandler.SetupResponseFactory(request =>
        {
            capturedPayload = request.Content?.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });

        // Act
        await service.AnalyzeResumeText(testText);

        // Assert
        capturedPayload.Should().NotBeNull();
        var payload = JsonDocument.Parse(capturedPayload!);
        payload.RootElement.GetProperty("inputs").GetString().Should().Be(testText);
    }

    [Fact]
    public async Task AnalyzeResumeText_SetsCorrectHeaders()
    {
        // Arrange
        var service = GetService();
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new object[] { });

        // Act
        await service.AnalyzeResumeText("Test text");

        // Assert
        var request = _factory.MockHuggingFaceHandler.ReceivedRequests.FirstOrDefault();
        request.Should().NotBeNull();
        request!.Headers.Authorization?.Scheme.Should().Be("Bearer");
        request.Headers.Authorization?.Parameter.Should().Be("test-api-key");
        request.Content?.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task AnalyzeResumeText_UsesCorrectEndpoint()
    {
        // Arrange
        var service = GetService();
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new object[] { });

        // Act
        await service.AnalyzeResumeText("Test");

        // Assert
        var request = _factory.MockHuggingFaceHandler.ReceivedRequests.FirstOrDefault();
        request?.RequestUri?.ToString().Should().Contain("dslim/bert-base-NER");
    }

    [Fact]
    public async Task AnalyzeResumeText_ReturnsRawJsonResponse()
    {
        // Arrange
        var service = GetService();
        var expectedResponse = new[]
        {
            new { entity_group = "PER", word = "John", score = 0.95f, start = 0, end = 4 }
        };
        _factory.MockHuggingFaceHandler.SetupJsonResponse(expectedResponse);

        // Act
        var result = await service.AnalyzeResumeText("John is a developer");

        // Assert
        result.Should().Contain("entity_group");
        result.Should().Contain("PER");
        result.Should().Contain("John");
    }

    [Fact]
    public async Task AnalyzeResumeText_WhenApiReturnsError_ThrowsException()
    {
        // Arrange
        var service = GetService();
        _factory.MockHuggingFaceHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.AnalyzeResumeText("Test text"));
    }

    [Fact]
    public async Task AnalyzeResumeText_HandlesEmptyResponse()
    {
        // Arrange
        var service = GetService();
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new object[] { });

        // Act
        var result = await service.AnalyzeResumeText("No entities here xyz abc");

        // Assert
        result.Should().Be("[]");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeResumeText_WithEmptyInput_StillMakesRequest(string input)
    {
        // Arrange
        var service = GetService();
        _factory.MockHuggingFaceHandler.SetupJsonResponse(new object[] { });

        // Act
        await service.AnalyzeResumeText(input);

        // Assert
        _factory.MockHuggingFaceHandler.ReceivedRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task AnalyzeResumeText_HandlesSpecialCharacters()
    {
        // Arrange
        var service = GetService();
        var textWithSpecialChars = "Developer with C# and .NET experience @ Microsoft";
        string? capturedPayload = null;

        _factory.MockHuggingFaceHandler.SetupResponseFactory(request =>
        {
            capturedPayload = request.Content?.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });

        // Act
        await service.AnalyzeResumeText(textWithSpecialChars);

        // Assert
        capturedPayload.Should().Contain("C#");
        capturedPayload.Should().Contain(".NET");
    }

    private HuggingFaceNlpService GetService()
    {
        _factory.MockHuggingFaceHandler.Clear();
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<HuggingFaceNlpService>();
    }
}
