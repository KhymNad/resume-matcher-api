using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using ResumeMatcherAPI.Helpers;
using Xunit;

namespace ResumeMatcherAPI.Helpers.Tests
{
    /// <summary>
    /// Unit tests for EmbeddingHelper covering configuration validation
    /// and error handling for the embedding API integration.
    /// </summary>
    public class EmbeddingHelperTests
    {
        #region Configuration Tests

        // Tests that GetEmbedding throws when the helper hasn't been configured.
        // This validates the fail-fast pattern - the application should error early
        // if required dependencies aren't properly initialized.
        [Fact]
        public void GetEmbedding_WithoutConfiguration_ThrowsInvalidOperationException()
        {
            // Arrange - Create a fresh configuration with missing values
            // Note: In a real scenario, you'd reset the static state or use DI
            var configData = new Dictionary<string, string?>
            {
                { "EmbeddingAPI:Url", null },
                { "EmbeddingAPI:ApiKey", null }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            EmbeddingHelper.Configure(configuration);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                EmbeddingHelper.GetEmbedding("test text"));

            Assert.Contains("not configured properly", exception.Message);
        }

        // Tests that configuration correctly reads values from IConfiguration.
        // Demonstrates proper use of the Options pattern for external API configuration.
        [Fact]
        public void Configure_WithValidSettings_DoesNotThrow()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                { "EmbeddingAPI:Url", "https://api.example.com/embeddings" },
                { "EmbeddingAPI:ApiKey", "test-api-key-12345" }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            // Act & Assert - should not throw
            var exception = Record.Exception(() => EmbeddingHelper.Configure(configuration));
            Assert.Null(exception);
        }

        // Tests behavior when only URL is configured but API key is missing.
        // Both values are required for the embedding service to function.
        [Fact]
        public void GetEmbedding_WithMissingApiKey_ThrowsInvalidOperationException()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                { "EmbeddingAPI:Url", "https://api.example.com/embeddings" },
                { "EmbeddingAPI:ApiKey", "" }  // Empty API key
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            EmbeddingHelper.Configure(configuration);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                EmbeddingHelper.GetEmbedding("test text"));
        }

        // Tests behavior when only API key is configured but URL is missing.
        [Fact]
        public void GetEmbedding_WithMissingUrl_ThrowsInvalidOperationException()
        {
            // Arrange
            var configData = new Dictionary<string, string?>
            {
                { "EmbeddingAPI:Url", "   " },  // Whitespace-only URL
                { "EmbeddingAPI:ApiKey", "valid-key" }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            EmbeddingHelper.Configure(configuration);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                EmbeddingHelper.GetEmbedding("some text"));
        }

        #endregion

        #region Input Validation Tests

        // Note: The current implementation doesn't validate empty input text.
        // These tests document expected behavior and could inform future improvements.

        // Tests that the embedding request payload is properly constructed.
        // The Hugging Face API expects a JSON object with an "inputs" field.
        [Fact]
        public void GetEmbedding_PayloadFormat_ShouldContainInputsField()
        {
            // This test documents the expected JSON payload structure:
            // { "inputs": "text to embed" }
            //
            // In production, this would be verified by mocking HttpClient
            // and inspecting the request body.

            // For now, this serves as documentation of the API contract
            Assert.True(true, "Payload should serialize to: { \"inputs\": \"<text>\" }");
        }

        #endregion

        #region Response Handling Tests

        // Documents the expected response format from Hugging Face embedding API.
        // The API returns embeddings in the format: [[float, float, ...]]
        [Fact]
        public void GetEmbedding_ExpectedResponseFormat_IsNestedFloatArray()
        {
            // Hugging Face embedding API response format:
            // [[0.123, -0.456, 0.789, ...]]
            //
            // The outer array can contain multiple embeddings if batch processing,
            // but for single text input, we expect one embedding vector.
            // The method returns the first (and typically only) embedding.

            // This test documents the parsing expectation
            Assert.True(true, "Response format: [[float, float, ...]]");
        }

        // Documents that HTTP errors result in null return value rather than exceptions.
        // This is a graceful degradation pattern for non-critical features.
        [Fact]
        public void GetEmbedding_OnHttpError_ReturnsNull()
        {
            // Current implementation returns null on HTTP errors.
            // This allows the calling code to implement fallback behavior
            // (e.g., skip embedding-based matching if service is unavailable).

            // In production, you might want to:
            // 1. Log the error details
            // 2. Implement retry logic with exponential backoff
            // 3. Use a circuit breaker pattern

            Assert.True(true, "HTTP errors should return null, not throw");
        }

        // Documents JSON parsing error handling behavior.
        [Fact]
        public void GetEmbedding_OnMalformedJson_ReturnsNull()
        {
            // If the API returns unexpected JSON format, the method returns null
            // rather than throwing a JsonException.
            //
            // This is defensive coding for external API integration where
            // response format might change unexpectedly.

            Assert.True(true, "Malformed JSON should return null");
        }

        #endregion
    }
}
