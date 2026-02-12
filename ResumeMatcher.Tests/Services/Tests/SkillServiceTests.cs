using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using ResumeMatcher.Tests.Integration;
using ResumeMatcherAPI.Services;
using Xunit;

namespace ResumeMatcher.Tests.Services.Tests
{
    /// <summary>
    /// Unit tests for SkillService covering configuration handling and interface contracts.
    /// Note: Database integration tests are in DatabaseIntegrationTests.cs
    /// </summary>
    public class SkillServiceTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidConfiguration_DoesNotThrow()
        {
            // Arrange
            var configuration = CreateMockConfiguration("Host=localhost;Database=test");

            // Act & Assert - Should not throw
            var service = new SkillService(configuration);
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_WithNullConnectionString_DoesNotThrowImmediately()
        {
            // Arrange - Connection string is null
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(c => c.GetSection("ConnectionStrings")["Supabase"])
                .Returns((string?)null);

            // Act & Assert - Should not throw during construction (lazy evaluation)
            var service = new SkillService(mockConfig.Object);
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_WithEmptyConnectionString_DoesNotThrowImmediately()
        {
            // Arrange
            var configuration = CreateMockConfiguration("");

            // Act & Assert - Should not throw during construction
            var service = new SkillService(configuration);
            Assert.NotNull(service);
        }

        #endregion

        #region Configuration Tests

        [Theory]
        [InlineData("Host=localhost;Database=skills;Username=user;Password=pass")]
        [InlineData("Server=db.example.com;Port=5432;Database=resume_db")]
        [InlineData("postgres://user:pass@host:5432/database")]
        public void Constructor_AcceptsVariousConnectionStringFormats(string connectionString)
        {
            // Arrange
            var configuration = CreateMockConfiguration(connectionString);

            // Act & Assert - Should accept any string format
            var service = new SkillService(configuration);
            Assert.NotNull(service);
        }

        #endregion

        #region GetAllSkillsAsync Error Handling Tests

        [Fact]
        public async Task GetAllSkillsAsync_WithInvalidConnectionString_ThrowsException()
        {
            // Arrange
            var configuration = CreateMockConfiguration("InvalidConnectionString");
            var service = new SkillService(configuration);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetAllSkillsAsync());
        }

        [Fact]
        public async Task GetAllSkillsAsync_WithNullConnectionString_ThrowsException()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(c => c.GetSection("ConnectionStrings")["Supabase"])
                .Returns((string?)null);
            var service = new SkillService(mockConfig.Object);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetAllSkillsAsync());
        }

        [Fact]
        public async Task GetAllSkillsAsync_WithUnreachableHost_ThrowsException()
        {
            // Arrange - Use an invalid host that won't be reachable
            var configuration = CreateMockConfiguration(
                "Host=invalid.nonexistent.host.example.com;Database=test;Username=user;Password=pass;Timeout=1");
            var service = new SkillService(configuration);

            // Act & Assert - Should throw due to connection failure
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetAllSkillsAsync());
        }

        #endregion

        #region Interface Contract Tests

        [Fact]
        public void GetAllSkillsAsync_ReturnsTaskOfListString()
        {
            // Arrange
            var configuration = CreateMockConfiguration("Host=localhost;Database=test");
            var service = new SkillService(configuration);

            // Act - Get the task (don't await, just verify it's assignable to Task<List<string>>)
            var task = service.GetAllSkillsAsync();

            // Assert
            Assert.IsAssignableFrom<Task<List<string>>>(task);
        }

        #endregion

        #region Helper Methods

        private IConfiguration CreateMockConfiguration(string connectionString)
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ConnectionStrings:Supabase", connectionString }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        #endregion
    }

    /// <summary>
    /// Integration tests for SkillService that require a real database connection.
    /// These tests should be run separately as they require external dependencies.
    /// </summary>
    public class SkillServiceIntegrationTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public SkillServiceIntegrationTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetAllSkillsAsync_WithSeededData_ReturnsSkills()
        {
            // Arrange
            await _factory.SeedDefaultSkillsAsync();

            // Note: SkillService uses Npgsql directly, not EF Core, so we test through
            // the Skills controller which uses the in-memory database
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/skills");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            Assert.NotEmpty(content);
        }
    }
}
