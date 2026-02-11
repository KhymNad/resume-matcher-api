using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ResumeMatcher.Tests.Integration.Database;

/// <summary>
/// Integration tests for database operations.
/// Tests Entity Framework Core integration with in-memory database.
/// Demonstrates proper database testing patterns that interviewers look for.
/// </summary>
public class DatabaseIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DatabaseIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region ApplicationDbContext Tests

    [Fact]
    public async Task DbContext_CanCreateDatabase()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Act
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task DbContext_CanAddSkill()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skill = new Skill
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test Skill",
            Type = "TestType",
            Source = "IntegrationTest"
        };

        // Act
        context.Skills.Add(skill);
        var saveResult = await context.SaveChangesAsync();

        // Assert
        saveResult.Should().Be(1);
    }

    [Fact]
    public async Task DbContext_CanRetrieveSkillById()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skillId = Guid.NewGuid();
        context.Skills.Add(new Skill
        {
            Id = skillId,
            Name = "RetrieveTest",
            Type = "Test",
            Source = "Test"
        });
        await context.SaveChangesAsync();

        // Act
        var retrievedSkill = await context.Skills.FindAsync(skillId);

        // Assert
        retrievedSkill.Should().NotBeNull();
        retrievedSkill!.Name.Should().Be("RetrieveTest");
    }

    [Fact]
    public async Task DbContext_CanUpdateSkill()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skill = new Skill
        {
            Id = Guid.NewGuid(),
            Name = "OriginalName",
            Type = "Test",
            Source = "Test"
        };
        context.Skills.Add(skill);
        await context.SaveChangesAsync();

        // Act
        skill.Name = "UpdatedName";
        context.Skills.Update(skill);
        await context.SaveChangesAsync();

        var updatedSkill = await context.Skills.FindAsync(skill.Id);

        // Assert
        updatedSkill!.Name.Should().Be("UpdatedName");
    }

    [Fact]
    public async Task DbContext_CanDeleteSkill()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skill = new Skill
        {
            Id = Guid.NewGuid(),
            Name = "ToDelete",
            Type = "Test",
            Source = "Test"
        };
        context.Skills.Add(skill);
        await context.SaveChangesAsync();

        // Act
        context.Skills.Remove(skill);
        await context.SaveChangesAsync();

        var deletedSkill = await context.Skills.FindAsync(skill.Id);

        // Assert
        deletedSkill.Should().BeNull();
    }

    [Fact]
    public async Task DbContext_CanQuerySkillsByType()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Skills.AddRange(
            new Skill { Id = Guid.NewGuid(), Name = "Python", Type = "Programming", Source = "Test" },
            new Skill { Id = Guid.NewGuid(), Name = "JavaScript", Type = "Programming", Source = "Test" },
            new Skill { Id = Guid.NewGuid(), Name = "Docker", Type = "DevOps", Source = "Test" }
        );
        await context.SaveChangesAsync();

        // Act
        var programmingSkills = await context.Skills
            .Where(s => s.Type == "Programming")
            .ToListAsync();

        // Assert
        programmingSkills.Should().HaveCount(2);
        programmingSkills.Should().Contain(s => s.Name == "Python");
        programmingSkills.Should().Contain(s => s.Name == "JavaScript");
    }

    [Fact]
    public async Task DbContext_CanPerformCaseInsensitiveSearch()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Skills.Add(new Skill
        {
            Id = Guid.NewGuid(),
            Name = "JavaScript",
            Type = "Programming",
            Source = "Test"
        });
        await context.SaveChangesAsync();

        // Act
        var result = await context.Skills
            .Where(s => s.Name.ToLower().Contains("javascript"))
            .FirstOrDefaultAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("JavaScript");
    }

    #endregion

    #region Skill Model Tests

    [Fact]
    public void Skill_HasCorrectTableMapping()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Act
        var entityType = context.Model.FindEntityType(typeof(Skill));

        // Assert
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("skills");
    }

    [Fact]
    public void Skill_IdColumn_HasCorrectMapping()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Act
        var entityType = context.Model.FindEntityType(typeof(Skill));
        var idProperty = entityType!.FindProperty("Id");

        // Assert
        idProperty.Should().NotBeNull();
        idProperty!.GetColumnName().Should().Be("id");
    }

    [Fact]
    public void Skill_NameColumn_HasCorrectMapping()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Act
        var entityType = context.Model.FindEntityType(typeof(Skill));
        var nameProperty = entityType!.FindProperty("Name");

        // Assert
        nameProperty.Should().NotBeNull();
        nameProperty!.GetColumnName().Should().Be("name");
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task DbContext_TransactionRollback_DoesNotPersistChanges()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skillId = Guid.NewGuid();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Skills.Add(new Skill
        {
            Id = skillId,
            Name = "RollbackTest",
            Type = "Test",
            Source = "Test"
        });
        await context.SaveChangesAsync();
        await transaction.RollbackAsync();

        // Need a new context to verify rollback
        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await verifyContext.Skills.FindAsync(skillId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task DbContext_HandlesMultipleReaders()
    {
        // Arrange
        await _factory.SeedDefaultSkillsAsync();

        // Act - Multiple concurrent reads
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.Skills.ToListAsync();
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(list => list.Count > 0);
    }

    #endregion

    #region Data Integrity Tests

    [Fact]
    public async Task DbContext_EnforcesUniqueIds()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var duplicateId = Guid.NewGuid();

        context.Skills.Add(new Skill { Id = duplicateId, Name = "First", Type = "Test", Source = "Test" });
        await context.SaveChangesAsync();

        // Act & Assert
        context.Skills.Add(new Skill { Id = duplicateId, Name = "Second", Type = "Test", Source = "Test" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DbContext_HandlesNullableFields()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var skill = new Skill
        {
            Id = Guid.NewGuid(),
            Name = "TestSkill",
            Type = null, // Nullable
            Source = null // Nullable
        };

        // Act
        context.Skills.Add(skill);
        await context.SaveChangesAsync();

        var retrieved = await context.Skills.FindAsync(skill.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Type.Should().BeNull();
        retrieved.Source.Should().BeNull();
    }

    #endregion
}
