using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ResumeMatcher.Tests.Integration.Fixtures;

/// <summary>
/// Helper methods for integration tests.
/// Provides utilities for creating HTTP content, parsing responses, and assertions.
/// </summary>
public static class TestHelpers
{
    #region HTTP Content Helpers

    /// <summary>
    /// Creates multipart form data for file upload endpoints.
    /// </summary>
    public static MultipartFormDataContent CreateFileUploadContent(
        string fileName,
        string fileContent,
        string contentType = "text/plain")
    {
        var formData = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);
        var byteContent = new ByteArrayContent(fileBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        formData.Add(byteContent, "file", fileName);
        return formData;
    }

    /// <summary>
    /// Creates multipart form data from a byte array (for binary files).
    /// </summary>
    public static MultipartFormDataContent CreateBinaryFileUploadContent(
        string fileName,
        byte[] fileBytes,
        string contentType)
    {
        var formData = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(fileBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        formData.Add(byteContent, "file", fileName);
        return formData;
    }

    /// <summary>
    /// Creates JSON content for POST requests.
    /// </summary>
    public static StringContent CreateJsonContent<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    #endregion

    #region Response Parsing Helpers

    /// <summary>
    /// Parses HTTP response content as JSON document.
    /// </summary>
    public static async Task<JsonDocument> ParseJsonResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    /// <summary>
    /// Deserializes HTTP response content to a specific type.
    /// </summary>
    public static async Task<T?> DeserializeResponseAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>
    /// Gets a specific property from a JSON response.
    /// </summary>
    public static async Task<string?> GetJsonPropertyAsync(
        HttpResponseMessage response,
        string propertyName)
    {
        using var doc = await ParseJsonResponseAsync(response);
        if (doc.RootElement.TryGetProperty(propertyName, out var property))
        {
            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.GetRawText();
        }
        return null;
    }

    #endregion

    #region Mock Response Builders

    /// <summary>
    /// Creates a mock NER response with specified entities.
    /// </summary>
    public static object[] CreateNerResponse(params (string entityGroup, string word, float score)[] entities)
    {
        int position = 0;
        return entities.Select(e =>
        {
            var entity = new
            {
                entity_group = e.entityGroup,
                word = e.word,
                score = e.score,
                start = position,
                end = position + e.word.Length
            };
            position += e.word.Length + 1;
            return (object)entity;
        }).ToArray();
    }

    /// <summary>
    /// Creates a mock Adzuna job response.
    /// </summary>
    public static object CreateJobResponse(params (string title, string company, string location)[] jobs)
    {
        return new
        {
            results = jobs.Select((j, i) => new
            {
                title = j.title,
                company = new { display_name = j.company },
                location = new { display_name = j.location },
                description = $"Job description for {j.title}",
                redirect_url = $"https://jobs.example.com/{i}"
            }).ToArray()
        };
    }

    #endregion

    #region Assertion Helpers

    /// <summary>
    /// Asserts that a JSON response contains a specific property.
    /// </summary>
    public static async Task AssertHasPropertyAsync(
        HttpResponseMessage response,
        string propertyName)
    {
        using var doc = await ParseJsonResponseAsync(response);
        if (!doc.RootElement.TryGetProperty(propertyName, out _))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected JSON response to contain property '{propertyName}' but it was not found.");
        }
    }

    /// <summary>
    /// Asserts that a JSON array property has a specific count.
    /// </summary>
    public static async Task AssertArrayLengthAsync(
        HttpResponseMessage response,
        string propertyName,
        int expectedLength)
    {
        using var doc = await ParseJsonResponseAsync(response);
        if (!doc.RootElement.TryGetProperty(propertyName, out var array))
        {
            throw new Xunit.Sdk.XunitException(
                $"Property '{propertyName}' not found in response.");
        }

        var actualLength = array.GetArrayLength();
        if (actualLength != expectedLength)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected array '{propertyName}' to have {expectedLength} elements but found {actualLength}.");
        }
    }

    #endregion

    #region Performance Helpers

    /// <summary>
    /// Measures execution time of an async operation.
    /// </summary>
    public static async Task<(T Result, long ElapsedMs)> MeasureAsync<T>(Func<Task<T>> operation)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await operation();
        stopwatch.Stop();
        return (result, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Runs multiple requests concurrently and returns timing statistics.
    /// </summary>
    public static async Task<ConcurrencyTestResult> RunConcurrentRequestsAsync(
        Func<Task<HttpResponseMessage>> requestFactory,
        int concurrentRequests)
    {
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(async _ =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await requestFactory();
                stopwatch.Stop();
                return new { Response = response, ElapsedMs = stopwatch.ElapsedMilliseconds };
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        return new ConcurrencyTestResult
        {
            TotalRequests = results.Length,
            SuccessfulRequests = results.Count(r => r.Response.IsSuccessStatusCode),
            FailedRequests = results.Count(r => !r.Response.IsSuccessStatusCode),
            AverageResponseTimeMs = results.Average(r => r.ElapsedMs),
            MaxResponseTimeMs = results.Max(r => r.ElapsedMs),
            MinResponseTimeMs = results.Min(r => r.ElapsedMs)
        };
    }

    public class ConcurrencyTestResult
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public long MaxResponseTimeMs { get; set; }
        public long MinResponseTimeMs { get; set; }
    }

    #endregion
}
