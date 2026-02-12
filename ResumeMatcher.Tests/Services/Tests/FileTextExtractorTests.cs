using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using ResumeMatcherAPI.Services;
using Xunit;

namespace ResumeMatcher.Tests.Services.Tests
{
    /// <summary>
    /// Unit tests for FileTextExtractor covering text extraction from various file formats.
    /// Tests focus on file type routing, error handling, and edge cases.
    /// </summary>
    public class FileTextExtractorTests
    {
        private readonly FileTextExtractor _extractor;

        public FileTextExtractorTests()
        {
            _extractor = new FileTextExtractor();
        }

        #region File Type Routing Tests

        [Fact]
        public async Task ExtractTextAsync_WithTxtFile_ExtractsTextDirectly()
        {
            // Arrange
            var content = "This is a sample resume text file content.";
            var mockFile = CreateMockFormFile("resume.txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        [Fact]
        public async Task ExtractTextAsync_WithUnsupportedFileType_ThrowsNotSupportedException()
        {
            // Arrange
            var mockFile = CreateMockFormFile("resume.xyz", "content", "application/octet-stream");

            // Act & Assert
            await Assert.ThrowsAsync<NotSupportedException>(
                () => _extractor.ExtractTextAsync(mockFile.Object));
        }

        [Theory]
        [InlineData(".jpg")]
        [InlineData(".png")]
        [InlineData(".gif")]
        [InlineData(".exe")]
        [InlineData(".zip")]
        [InlineData(".html")]
        public async Task ExtractTextAsync_WithUnsupportedExtensions_ThrowsNotSupportedException(string extension)
        {
            // Arrange
            var mockFile = CreateMockFormFile($"resume{extension}", "content", "application/octet-stream");

            // Act & Assert
            await Assert.ThrowsAsync<NotSupportedException>(
                () => _extractor.ExtractTextAsync(mockFile.Object));
        }

        [Theory]
        [InlineData(".TXT")]
        [InlineData(".Txt")]
        [InlineData(".tXt")]
        public async Task ExtractTextAsync_WithTxtExtension_IsCaseInsensitive(string extension)
        {
            // Arrange
            var content = "Resume content";
            var mockFile = CreateMockFormFile($"resume{extension}", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        #endregion

        #region TXT File Tests

        [Fact]
        public async Task ExtractTextAsync_WithEmptyTxtFile_ReturnsEmptyString()
        {
            // Arrange
            var mockFile = CreateMockFormFile("resume.txt", "", "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task ExtractTextAsync_WithLargeTxtFile_ExtractsAllContent()
        {
            // Arrange
            var content = new string('A', 100000); // 100KB of text
            var mockFile = CreateMockFormFile("resume.txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content.Length, result.Length);
        }

        [Fact]
        public async Task ExtractTextAsync_WithTxtFile_PreservesUnicodeCharacters()
        {
            // Arrange
            var content = "Résumé with special chars: é, ü, ñ, 日本語, 한국어";
            var mockFile = CreateMockFormFile("resume.txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        [Fact]
        public async Task ExtractTextAsync_WithTxtFile_PreservesLineBreaks()
        {
            // Arrange
            var content = "Line 1\nLine 2\r\nLine 3\rLine 4";
            var mockFile = CreateMockFormFile("resume.txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        [Fact]
        public async Task ExtractTextAsync_WithTxtFile_HandlesWhitespaceOnlyContent()
        {
            // Arrange
            var content = "   \t\n   ";
            var mockFile = CreateMockFormFile("resume.txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        #endregion

        #region Edge Cases

        [Theory]
        [InlineData("resume.test.txt")]
        [InlineData("my.resume.final.txt")]
        [InlineData("...txt")]
        public async Task ExtractTextAsync_WithMultipleDotsInFilename_UsesLastExtension(string filename)
        {
            // Arrange
            var content = "Resume content";
            var mockFile = CreateMockFormFile(filename, content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        [Fact]
        public async Task ExtractTextAsync_WithSpecialCharsInFilename_ProcessesCorrectly()
        {
            // Arrange
            var content = "Resume content";
            var mockFile = CreateMockFormFile("John's Resume (2024).txt", content, "text/plain");

            // Act
            var result = await _extractor.ExtractTextAsync(mockFile.Object);

            // Assert
            Assert.Equal(content, result);
        }

        #endregion

        #region Concurrency Tests

        [Fact]
        public async Task ExtractTextAsync_MultipleParallelRequests_HandlesCorrectly()
        {
            // Arrange
            var tasks = new Task<string>[10];
            for (int i = 0; i < 10; i++)
            {
                var content = $"Resume content {i}";
                var mockFile = CreateMockFormFile($"resume{i}.txt", content, "text/plain");
                tasks[i] = _extractor.ExtractTextAsync(mockFile.Object);
            }

            // Act
            var results = await Task.WhenAll(tasks);

            // Assert
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal($"Resume content {i}", results[i]);
            }
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

    /// <summary>
    /// Additional tests for FileTextExtractor error scenarios
    /// </summary>
    public class FileTextExtractorErrorTests
    {
        private readonly FileTextExtractor _extractor;

        public FileTextExtractorErrorTests()
        {
            _extractor = new FileTextExtractor();
        }

        [Fact]
        public async Task ExtractTextAsync_WithDisposedStream_ThrowsException()
        {
            // Arrange
            var mock = new Mock<IFormFile>();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            stream.Dispose(); // Dispose the stream

            mock.Setup(f => f.FileName).Returns("resume.txt");
            mock.Setup(f => f.OpenReadStream()).Returns(stream);

            // Act & Assert - Disposed stream throws ArgumentException ("Stream was not readable")
            await Assert.ThrowsAsync<ArgumentException>(
                () => _extractor.ExtractTextAsync(mock.Object));
        }

        [Fact]
        public async Task ExtractTextAsync_WithNullFileName_ThrowsException()
        {
            // Arrange
            var mock = new Mock<IFormFile>();
            mock.Setup(f => f.FileName).Returns((string)null!);

            // Act & Assert
            await Assert.ThrowsAsync<NullReferenceException>(
                () => _extractor.ExtractTextAsync(mock.Object));
        }
    }
}
