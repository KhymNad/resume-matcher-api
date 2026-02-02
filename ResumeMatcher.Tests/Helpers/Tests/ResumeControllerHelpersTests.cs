using System;
using System.Collections.Generic;
using System.Linq;
using ResumeMatcherAPI.Controllers;
using ResumeMatcherAPI.Helpers;
using Xunit;

namespace ResumeMatcherAPI.Helpers.Tests
{
    /// <summary>
    /// Unit tests for ResumeControllerHelpers covering text processing,
    /// NLP entity handling, and skill extraction logic.
    /// </summary>
    public class ResumeControllerHelpersTests
    {
        #region SplitTextIntoChunks Tests

        // Tests that text chunking correctly splits long text while preserving word boundaries.
        // This is important when processing large resumes through APIs with character limits.
        [Fact]
        public void SplitTextIntoChunks_WithLongText_SplitsAtWhitespace()
        {
            // Arrange
            string text = "This is a sample resume text that needs to be split into smaller chunks for processing";
            int maxChunkSize = 30;

            // Act
            var chunks = ResumeControllerHelpers.SplitTextIntoChunks(text, maxChunkSize).ToList();

            // Assert
            Assert.True(chunks.All(c => c.Length <= maxChunkSize));
            Assert.True(chunks.Count > 1);
        }

        // Verifies that empty or null input returns no chunks without throwing exceptions.
        // Defensive programming pattern for handling edge cases in text processing.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SplitTextIntoChunks_WithEmptyOrNullInput_ReturnsEmpty(string input)
        {
            // Act
            var chunks = ResumeControllerHelpers.SplitTextIntoChunks(input).ToList();

            // Assert
            Assert.Empty(chunks);
        }

        // Confirms that text smaller than the chunk size is returned as a single chunk.
        [Fact]
        public void SplitTextIntoChunks_WithShortText_ReturnsSingleChunk()
        {
            // Arrange
            string text = "Short text";

            // Act
            var chunks = ResumeControllerHelpers.SplitTextIntoChunks(text, 1000).ToList();

            // Assert
            Assert.Single(chunks);
            Assert.Equal("Short text", chunks[0]);
        }

        #endregion

        #region SimplifyEntityLabel Tests

        // Tests BIO (Beginning-Inside-Outside) label simplification used in NER (Named Entity Recognition).
        // NER models often output labels like "B-SKILL" or "I-SKILL" to indicate token position in an entity.
        [Theory]
        [InlineData("B-SKILL", "SKILL")]
        [InlineData("I-SKILL", "SKILL")]
        [InlineData("B-PER", "PER")]
        [InlineData("I-ORG", "ORG")]
        public void SimplifyEntityLabel_WithBIOPrefix_RemovesPrefix(string input, string expected)
        {
            // Act
            var result = ResumeControllerHelpers.SimplifyEntityLabel(input);

            // Assert
            Assert.Equal(expected, result);
        }

        // Labels without BIO prefixes should pass through unchanged.
        [Theory]
        [InlineData("SKILL")]
        [InlineData("PERSON")]
        [InlineData("ORG")]
        public void SimplifyEntityLabel_WithoutPrefix_ReturnsUnchanged(string input)
        {
            // Act
            var result = ResumeControllerHelpers.SimplifyEntityLabel(input);

            // Assert
            Assert.Equal(input, result);
        }

        #endregion

        #region CleanSkillList Tests

        // Validates skill cleaning logic: removes short words, banned common words,
        // normalizes casing to Title Case, and eliminates duplicates.
        [Fact]
        public void CleanSkillList_RemovesBannedWordsAndDuplicates()
        {
            // Arrange
            var skills = new List<string> { "Python", "python", "PYTHON", "team", "JavaScript", "work" };

            // Act
            var result = ResumeControllerHelpers.CleanSkillList(skills);

            // Assert - removes duplicates (case-insensitive) and banned words
            Assert.Contains("Python", result);
            Assert.Contains("Javascript", result); // Title case
            Assert.DoesNotContain("team", result);
            Assert.DoesNotContain("work", result);
        }

        // Short skill names (2 chars or less) should be filtered out as they're usually noise.
        [Fact]
        public void CleanSkillList_RemovesShortSkills()
        {
            // Arrange
            var skills = new List<string> { "C", "Go", "SQL", "AI" };

            // Act
            var result = ResumeControllerHelpers.CleanSkillList(skills);

            // Assert - "C", "Go", "AI" are 2 chars or less
            Assert.DoesNotContain("C", result);
            Assert.Contains("Sql", result);
        }

        #endregion

        #region CleanWord Tests

        // Tests tokenizer artifact removal. Models like BERT use "##" to indicate subword tokens.
        // Example: "JavaScript" might be tokenized as ["Java", "##Script"].
        [Theory]
        [InlineData("##Script", "Script")]
        [InlineData("Python.", "Python")]
        [InlineData("  React  ", "React")]
        [InlineData("C++", "C++")]  // Programming language names preserved
        [InlineData("C#", "C#")]
        public void CleanWord_RemovesTokenArtifactsAndSpecialChars(string input, string expected)
        {
            // Act
            var result = ResumeControllerHelpers.CleanWord(input);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region MapToCategory Tests

        // Tests entity label to category mapping. NER models output various entity types
        // that need to be normalized to application-specific categories.
        [Theory]
        [InlineData("PER", "Persons")]
        [InlineData("PERSON", "Persons")]
        [InlineData("LOC", "Locations")]
        [InlineData("LOCATION", "Locations")]
        [InlineData("SKILL", "Skills")]
        [InlineData("TECH", "Skills")]
        [InlineData("ORG", "Skills")]  // Organizations mapped to Skills in this context
        [InlineData("JOB", "WorkExperience")]
        [InlineData("ROLE", "WorkExperience")]
        [InlineData("EDU", "Education")]
        [InlineData("DEGREE", "Education")]
        [InlineData("UNKNOWN", "Other")]
        public void MapToCategory_MapsLabelsCorrectly(string entityLabel, string expectedCategory)
        {
            // Act
            var result = ResumeControllerHelpers.MapToCategory(entityLabel);

            // Assert
            Assert.Equal(expectedCategory, result);
        }

        // Category mapping should be case-insensitive for robustness.
        [Fact]
        public void MapToCategory_IsCaseInsensitive()
        {
            Assert.Equal("Skills", ResumeControllerHelpers.MapToCategory("skill"));
            Assert.Equal("Skills", ResumeControllerHelpers.MapToCategory("SKILL"));
            Assert.Equal("Skills", ResumeControllerHelpers.MapToCategory("Skill"));
        }

        #endregion

        #region MergeConsecutiveEntities Tests

        // Tests entity merging: consecutive tokens with the same label should be combined.
        // Example: ["Micro", "##soft"] with label SKILL should become "Microsoft".
        [Fact]
        public void MergeConsecutiveEntities_CombinesConsecutiveTokens()
        {
            // Arrange
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "B-SKILL", Word = "Micro", Start = 0, End = 5, Score = 0.95f },
                new() { Entity = "I-SKILL", Word = "soft", Start = 5, End = 9, Score = 0.90f }
            };

            // Act
            var result = ResumeControllerHelpers.MergeConsecutiveEntities(entities);

            // Assert
            Assert.Single(result);
            Assert.Equal("Microsoft", result[0].Word);
            Assert.Equal(0, result[0].Start);
            Assert.Equal(9, result[0].End);
        }

        // Entities with different labels should remain separate after merging.
        [Fact]
        public void MergeConsecutiveEntities_KeepsDifferentEntitiesSeparate()
        {
            // Arrange
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "B-SKILL", Word = "Python", Start = 0, End = 6, Score = 0.95f },
                new() { Entity = "B-ORG", Word = "Google", Start = 10, End = 16, Score = 0.90f }
            };

            // Act
            var result = ResumeControllerHelpers.MergeConsecutiveEntities(entities);

            // Assert
            Assert.Equal(2, result.Count);
        }

        // Empty input should return empty output without throwing.
        [Fact]
        public void MergeConsecutiveEntities_WithEmptyList_ReturnsEmpty()
        {
            // Act
            var result = ResumeControllerHelpers.MergeConsecutiveEntities(new List<HuggingFaceEntity>());

            // Assert
            Assert.Empty(result);
        }

        // Merged entity should retain the minimum confidence score (most conservative approach).
        [Fact]
        public void MergeConsecutiveEntities_RetainsMinimumScore()
        {
            // Arrange
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "B-SKILL", Word = "Java", Start = 0, End = 4, Score = 0.95f },
                new() { Entity = "I-SKILL", Word = "Script", Start = 4, End = 10, Score = 0.80f }
            };

            // Act
            var result = ResumeControllerHelpers.MergeConsecutiveEntities(entities);

            // Assert
            Assert.Equal(0.80f, result[0].Score);
        }

        #endregion

        #region AdjustEntityOffsets Tests

        // Tests offset adjustment when processing text in chunks.
        // Chunk-local positions must be converted to global positions relative to full text.
        [Fact]
        public void AdjustEntityOffsets_CorrectlyAdjustsPositions()
        {
            // Arrange
            string fullText = "Hello World. Python is great.";
            string chunk = "Python is great.";
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "SKILL", Word = "Python", Start = 0, End = 6, Score = 0.9f }
            };

            // Act
            ResumeControllerHelpers.AdjustEntityOffsets(entities, fullText, chunk);

            // Assert - "Python" starts at index 13 in fullText
            Assert.Equal(13, entities[0].Start);
            Assert.Equal(19, entities[0].End);
        }

        #endregion

        #region GroupAndCleanEntities Tests

        // Tests the complete pipeline: grouping by category, filtering by confidence,
        // cleaning words, and removing duplicates.
        [Fact]
        public void GroupAndCleanEntities_GroupsByCategory()
        {
            // Arrange
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "SKILL", Word = "Python", Start = 0, End = 6, Score = 0.95f },
                new() { Entity = "SKILL", Word = "JavaScript", Start = 10, End = 20, Score = 0.90f },
                new() { Entity = "PERSON", Word = "John", Start = 25, End = 29, Score = 0.85f }
            };

            // Act
            var result = ResumeControllerHelpers.GroupAndCleanEntities(entities);

            // Assert
            Assert.True(result.ContainsKey("Skills"));
            Assert.True(result.ContainsKey("Persons"));
            Assert.Equal(2, result["Skills"].Count);
        }

        // Entities below the confidence threshold (0.70) should be filtered out.
        [Fact]
        public void GroupAndCleanEntities_FiltersLowConfidenceEntities()
        {
            // Arrange
            var entities = new List<HuggingFaceEntity>
            {
                new() { Entity = "SKILL", Word = "Python", Start = 0, End = 6, Score = 0.95f },
                new() { Entity = "SKILL", Word = "JavaScript", Start = 10, End = 20, Score = 0.50f } // Low score
            };

            // Act
            var result = ResumeControllerHelpers.GroupAndCleanEntities(entities);

            // Assert
            Assert.Single(result["Skills"]);
            Assert.Contains("Python", result["Skills"]);
        }

        #endregion

        #region FallbackEducationDetection Tests

        // Tests regex-based fallback when NER model fails to detect education entities.
        // Pattern matching for common education keywords provides backup extraction.
        [Fact]
        public void FallbackEducationDetection_DetectsEducationKeywords()
        {
            // Arrange
            string resumeText = "I graduated from Stanford University with a Bachelor in Computer Science";
            var groupedEntities = new Dictionary<string, List<string>>();

            // Act
            ResumeControllerHelpers.FallbackEducationDetection(resumeText, groupedEntities);

            // Assert
            Assert.True(groupedEntities.ContainsKey("Education"));
            Assert.Contains("University", groupedEntities["Education"]);
            Assert.Contains("Bachelor", groupedEntities["Education"]);
        }

        // Fallback should not override existing education data from NER.
        [Fact]
        public void FallbackEducationDetection_DoesNotOverrideExisting()
        {
            // Arrange
            string resumeText = "I graduated from Stanford University with a Bachelor in CS";
            var groupedEntities = new Dictionary<string, List<string>>
            {
                { "Education", new List<string> { "Stanford University" } }
            };

            // Act
            ResumeControllerHelpers.FallbackEducationDetection(resumeText, groupedEntities);

            // Assert - should keep original, not override
            Assert.Single(groupedEntities["Education"]);
            Assert.Contains("Stanford University", groupedEntities["Education"]);
        }

        // Verifies detection of various education-related patterns.
        [Theory]
        [InlineData("B.Sc in Physics")]
        [InlineData("M.Sc in Chemistry")]
        [InlineData("Diploma in IT")]
        [InlineData("College of Engineering")]
        public void FallbackEducationDetection_MatchesVariousPatterns(string resumeText)
        {
            // Arrange
            var groupedEntities = new Dictionary<string, List<string>>();

            // Act
            ResumeControllerHelpers.FallbackEducationDetection(resumeText, groupedEntities);

            // Assert
            Assert.True(groupedEntities.ContainsKey("Education"));
            Assert.NotEmpty(groupedEntities["Education"]);
        }

        #endregion
    }
}
