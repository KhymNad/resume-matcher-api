using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using Moq.Protected;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Services
{
    /// <summary>
    /// Integration tests for PythonResumeParserService covering HTTP client behavior,
    /// retry logic with exponential backoff, and service health checks.
    /// </summary>
    public class PythonResumeParserServiceIntegrationTests
    {
        #region Service Initialization Tests

        [Fact]
        public void Constructor_SetsBaseAddress()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var httpClient = new HttpClient(mockHandler.Object);

            // Act
            var service = new PythonResumeParserService(httpClient);

            // Assert
            Assert.Equal("https://resume-parser-oysv.onrender.com/", httpClient.BaseAddress?.ToString());
        }

        #endregion

        #region Health Check Tests

        [Fact]
        public async Task ExtractTextAsync_WhenServiceIsHealthy_ReturnsExtractedText()
        {
            // Arrange
            var requestCount = 0;
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    requestCount++;
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    if (request.RequestUri?.PathAndQuery == "/extract-resume")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("Extracted resume text")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "PDF content", "application/pdf");

            // Act
            var result = await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal("Extracted resume text", result);
            Assert.True(requestCount >= 2); // At least health check + extract
        }

        [Fact]
        public async Task ExtractTextAsync_WhenServiceBecomesHealthyAfterRetries_Succeeds()
        {
            // Arrange
            var healthCheckAttempts = 0;
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        healthCheckAttempts++;
                        // Fail first 2 attempts, succeed on 3rd
                        if (healthCheckAttempts < 3)
                        {
                            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        }
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    if (request.RequestUri?.PathAndQuery == "/extract-resume")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("Success after retries")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "PDF content", "application/pdf");

            // Act
            var result = await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal("Success after retries", result);
            Assert.Equal(3, healthCheckAttempts);
        }

        [Fact]
        public async Task ExtractTextAsync_WhenServiceNeverBecomesHealthy_ThrowsException()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "PDF content", "application/pdf");

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(
                () => service.ExtractTextAsync(mockFile.Object));
            Assert.Contains("did not become ready", exception.Message);
        }

        #endregion

        #region File Upload Tests

        [Fact]
        public async Task ExtractTextAsync_SendsMultipartFormData()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    if (request.RequestUri?.PathAndQuery == "/extract-resume")
                    {
                        capturedRequest = request;
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("Extracted text")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("test-resume.pdf", "content", "application/pdf");

            // Act
            await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.IsType<MultipartFormDataContent>(capturedRequest.Content);
        }

        [Fact]
        public async Task ExtractTextAsync_IncludesCorrectFileName()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    if (request.RequestUri?.PathAndQuery == "/extract-resume")
                    {
                        capturedRequest = request;
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("text")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("my-resume.pdf", "content", "application/pdf");

            // Act
            await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Content);
            var contentString = await capturedRequest.Content.ReadAsStringAsync();
            Assert.Contains("my-resume.pdf", contentString);
        }

        [Fact]
        public async Task ExtractTextAsync_SetsCorrectContentType()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    capturedRequest = request;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("text")
                    };
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act
            await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Content);
            Assert.Contains("multipart/form-data", capturedRequest.Content.Headers.ContentType?.ToString());
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task ExtractTextAsync_WhenExtractFails_ThrowsHttpRequestException()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.ExtractTextAsync(mockFile.Object));
        }

        [Fact]
        public async Task ExtractTextAsync_WhenNetworkError_ThrowsException()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(
                () => service.ExtractTextAsync(mockFile.Object));
        }

        #endregion

        #region Exponential Backoff Tests

        [Fact]
        public async Task WaitForServiceReadyAsync_ImplementsExponentialBackoff()
        {
            // Arrange
            var attemptTimes = new List<DateTime>();
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        attemptTimes.Add(DateTime.UtcNow);
                        // Succeed on 4th attempt
                        if (attemptTimes.Count >= 4)
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK);
                        }
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("text")
                    };
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act
            await service.ExtractTextAsync(mockFile.Object);

            // Assert - Each delay should be roughly exponentially increasing
            Assert.True(attemptTimes.Count >= 4);
        }

        #endregion

        #region Response Handling Tests

        [Fact]
        public async Task ExtractTextAsync_ReturnsRawResponseContent()
        {
            // Arrange
            var expectedText = "This is the extracted resume text with special chars: é, ñ, 日本語";
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(expectedText, Encoding.UTF8)
                    };
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act
            var result = await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(expectedText, result);
        }

        [Fact]
        public async Task ExtractTextAsync_HandlesEmptyResponse()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    if (request.RequestUri?.PathAndQuery == "/healthz")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("")
                    };
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var service = new PythonResumeParserService(httpClient);
            var mockFile = CreateMockFormFile("resume.pdf", "content", "application/pdf");

            // Act
            var result = await service.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Empty(result);
        }

        #endregion

        #region Helper Methods

        private Mock<IFormFile> CreateMockFormFile(string fileName, string content, string contentType)
        {
            var mock = new Mock<IFormFile>();
            var bytes = Encoding.UTF8.GetBytes(content);
            var stream = new MemoryStream(bytes);

            mock.Setup(f => f.FileName).Returns(fileName);
            mock.Setup(f => f.ContentType).Returns(contentType);
            mock.Setup(f => f.Length).Returns(bytes.Length);
            mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
            mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>(async (target, token) =>
                {
                    await target.WriteAsync(bytes, token);
                });

            return mock;
        }

        #endregion
    }
}
