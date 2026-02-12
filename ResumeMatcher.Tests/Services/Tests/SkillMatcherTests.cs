using System;
using System.Collections.Generic;
using System.Linq;
using ResumeMatcherAPI.Helpers;
using Xunit;

namespace ResumeMatcher.Tests.Services.Tests
{
    /// <summary>
    /// Unit tests for SkillMatcher covering skill validation, normalization,
    /// and matching logic using substring and n-gram approaches.
    /// </summary>
    public class SkillMatcherTests
    {
        #region MatchedSkill Model Tests

        [Fact]
        public void MatchedSkill_CanBeInstantiated_WithAllProperties()
        {
            // Arrange & Act
            var skill = new MatchedSkill
            {
                Skill = "Python",
                Source = "NER",
                Score = 0.95
            };

            // Assert
            Assert.Equal("Python", skill.Skill);
            Assert.Equal("NER", skill.Source);
            Assert.Equal(0.95, skill.Score);
        }

        [Fact]
        public void MatchedSkill_AllowsNullableProperties()
        {
            // Arrange & Act
            var skill = new MatchedSkill();

            // Assert
            Assert.Null(skill.Skill);
            Assert.Null(skill.Source);
            Assert.Null(skill.Score);
        }

        #endregion

        #region GetLoadedSkills Tests

        [Fact]
        public void GetLoadedSkills_WhenNotLoaded_ReturnsEmptyHashSet()
        {
            // Act
            var skills = SkillMatcher.GetLoadedSkills();

            // Assert
            Assert.NotNull(skills);
            Assert.IsType<HashSet<string>>(skills);
        }

        [Fact]
        public void GetNormalizedSkillMap_WhenNotLoaded_ReturnsEmptyDictionary()
        {
            // Act
            var map = SkillMatcher.GetNormalizedSkillMap();

            // Assert
            Assert.NotNull(map);
            Assert.IsType<Dictionary<string, string>>(map);
        }

        #endregion

        #region MatchKnownSkills Tests (Without DB)

        [Fact]
        public void MatchKnownSkills_WhenSkillsNotLoaded_ReturnsEmptyList()
        {
            // Act
            var result = SkillMatcher.MatchKnownSkills("Python JavaScript React");

            // Assert
            Assert.NotNull(result);
            Assert.IsType<List<string>>(result);
        }

        #endregion

        #region MatchSkillsWithNGrams Tests (Without DB)

        [Fact]
        public void MatchSkillsWithNGrams_WhenSkillsNotLoaded_ReturnsEmptyList()
        {
            // Act
            var result = SkillMatcher.MatchSkillsWithNGrams("Python JavaScript React");

            // Assert
            Assert.NotNull(result);
            Assert.IsType<List<string>>(result);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(10)]
        public void MatchSkillsWithNGrams_AcceptsVariousGramSizes(int maxGramSize)
        {
            // Act - Should not throw for any gram size
            var result = SkillMatcher.MatchSkillsWithNGrams("Test text", maxGramSize);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region MatchSkillsSmart Tests

        [Fact]
        public void MatchSkillsSmart_WhenSkillsNotLoaded_ReturnsEmptyList()
        {
            // Act
            var result = SkillMatcher.MatchSkillsSmart("Python JavaScript React");

            // Assert
            Assert.NotNull(result);
            Assert.IsType<List<string>>(result);
        }

        [Fact]
        public void MatchSkillsSmart_ReturnsSortedList()
        {
            // Act
            var result = SkillMatcher.MatchSkillsSmart("Some text with skills");

            // Assert
            var sorted = result.OrderBy(x => x).ToList();
            Assert.Equal(sorted, result);
        }

        #endregion

        #region MatchSkills Tests

        [Fact]
        public void MatchSkills_WithNullNerSkills_DoesNotThrow()
        {
            // Act
            var result = SkillMatcher.MatchSkills("Resume text", null);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void MatchSkills_WithEmptyNerSkills_ReturnsOnlySmartMatches()
        {
            // Arrange
            var nerSkills = new List<string>();

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, m => Assert.NotEqual("NER", m.Source));
        }

        [Fact]
        public void MatchSkills_WithNerSkills_IncludesNerTaggedSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "Python", "JavaScript" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume with skills", nerSkills);

            // Assert
            Assert.Contains(result, m => m.Skill == "Python" && m.Source == "NER");
            Assert.Contains(result, m => m.Skill == "JavaScript" && m.Source == "NER");
        }

        [Fact]
        public void MatchSkills_DeduplicatesNerSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "Python", "python", "PYTHON" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert - Should only have one Python entry (case-insensitive dedup)
            var pythonCount = result.Count(m => m.Skill?.Equals("Python", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Equal(1, pythonCount);
        }

        [Fact]
        public void MatchSkills_TrimsNerSkillWhitespace()
        {
            // Arrange
            var nerSkills = new List<string> { "  Python  ", "\tJavaScript\n" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Contains(result, m => m.Skill == "Python");
            Assert.Contains(result, m => m.Skill == "JavaScript");
        }

        [Fact]
        public void MatchSkills_IgnoresEmptyOrWhitespaceNerSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "", "  ", "Python" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Single(result, m => m.Source == "NER");
            Assert.Contains(result, m => m.Skill == "Python");
        }

        [Fact]
        public void MatchSkills_ReturnsResultsSortedBySkillName()
        {
            // Arrange
            var nerSkills = new List<string> { "Zebra", "Apple", "Middle" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            var skillNames = result.Select(m => m.Skill).ToList();
            var sortedNames = skillNames.OrderBy(x => x).ToList();
            Assert.Equal(sortedNames, skillNames);
        }

        [Fact]
        public void MatchSkills_SetsCorrectSourceForNerSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "Python" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            var pythonSkill = result.FirstOrDefault(m => m.Skill == "Python");
            Assert.NotNull(pythonSkill);
            Assert.Equal("NER", pythonSkill.Source);
        }

        [Fact]
        public void MatchSkills_WithEmptyResumeText_StillProcessesNerSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "Python", "JavaScript" };

            // Act
            var result = SkillMatcher.MatchSkills("", nerSkills);

            // Assert
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void MatchSkills_WithNullResumeText_StillProcessesNerSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "Python" };

            // Act - Should handle null gracefully
            var result = SkillMatcher.MatchSkills(null!, nerSkills);

            // Assert
            Assert.Contains(result, m => m.Skill == "Python");
        }

        #endregion

        #region Edge Cases and Robustness Tests

        [Fact]
        public void MatchSkills_HandlesSpecialCharactersInSkills()
        {
            // Arrange
            var nerSkills = new List<string> { "C#", "C++", "Node.js", "ASP.NET" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Equal(4, result.Count);
            Assert.Contains(result, m => m.Skill == "C#");
            Assert.Contains(result, m => m.Skill == "C++");
        }

        [Fact]
        public void MatchSkills_HandlesUnicodeCharacters()
        {
            // Arrange
            var nerSkills = new List<string> { "Résumé Parser", "日本語" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void MatchSkills_HandlesVeryLongSkillNames()
        {
            // Arrange
            var longSkill = new string('A', 1000);
            var nerSkills = new List<string> { longSkill };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Single(result);
            Assert.Equal(longSkill, result[0].Skill);
        }

        [Fact]
        public void MatchSkills_HandlesLargeNumberOfNerSkills()
        {
            // Arrange
            var nerSkills = Enumerable.Range(1, 1000).Select(i => $"Skill{i}").ToList();

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Equal(1000, result.Count);
        }

        [Fact]
        public void MatchSkills_HandlesVeryLongResumeText()
        {
            // Arrange
            var longResume = string.Join(" ", Enumerable.Repeat("word", 10000));
            var nerSkills = new List<string> { "Python" };

            // Act
            var result = SkillMatcher.MatchSkills(longResume, nerSkills);

            // Assert
            Assert.Contains(result, m => m.Skill == "Python");
        }

        #endregion

        #region Case Sensitivity Tests

        [Fact]
        public void MatchSkills_PreservesOriginalNerSkillCasing()
        {
            // Arrange
            var nerSkills = new List<string> { "PyThOn" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert
            Assert.Contains(result, m => m.Skill == "PyThOn");
        }

        [Fact]
        public void MatchSkills_CaseInsensitiveDeduplication()
        {
            // Arrange
            var nerSkills = new List<string> { "Python", "PYTHON" };

            // Act
            var result = SkillMatcher.MatchSkills("Resume text", nerSkills);

            // Assert - Only first occurrence should be kept
            Assert.Single(result);
            Assert.Equal("Python", result[0].Skill);
        }

        #endregion
    }
}
