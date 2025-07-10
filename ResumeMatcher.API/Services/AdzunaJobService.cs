using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace ResumeMatcherAPI.Services
{
    public class AdzunaJobService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        // Constructor injects HttpClient and IConfiguration for API calls and config access
        public AdzunaJobService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        // Searches Adzuna jobs for multiple skills and locations
        // Aggregates unique jobs based on RedirectUrl to avoid duplicates
        public async Task<List<JobListing>> SearchJobsAsync(List<string> skills, List<string> locations)
        {
            // Read Adzuna API credentials from configuration
            string appId = _config["Adzuna:AppId"]!;
            string appKey = _config["Adzuna:AppKey"]!;

            // Dictionary to hold unique jobs keyed by RedirectUrl (job URL)
            var allJobs = new Dictionary<string, JobListing>();

            // Clean and get distinct locations (ignore empty/null strings)
            var uniqueLocations = locations
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Clean and get distinct skills; limit to top 5 for performance
            var uniqueSkills = skills
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            // Loop through each skill
            foreach (var skill in uniqueSkills)
            {
                // URL encode skill for safe query string usage
                string what = Uri.EscapeDataString(skill);

                // Loop through each location
                foreach (var loc in uniqueLocations)
                {
                    // URL encode location for safe query string usage
                    string where = Uri.EscapeDataString(loc);

                    // Build the Adzuna API search URL with parameters
                    string url = $"https://api.adzuna.com/v1/api/jobs/ca/search/1?app_id={appId}&app_key={appKey}&results_per_page=10&what={what}&where={where}";

                    Console.WriteLine($"Querying: {url}");  // Debug output for tracking queries

                    try
                    {
                        // Send HTTP GET request to Adzuna API
                        var response = await _httpClient.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            // Log failed request status and continue with next iteration
                            Console.WriteLine($"Failed request: {response.StatusCode} for {url}");
                            continue;
                        }

                        // Read response content as bytes and decode to UTF-8 string
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        var json = Encoding.UTF8.GetString(bytes);

                        // Debug output: raw JSON response from Adzuna
                        Console.WriteLine("Raw Adzuna API response JSON:");
                        Console.WriteLine(json);

                        // Deserialize JSON response into AdzunaResponse object
                        var jobData = JsonSerializer.Deserialize<AdzunaResponse>(json);

                        if (jobData?.Results != null)
                        {
                            // Iterate over each job result
                            foreach (var job in jobData.Results)
                            {
                                // Add unique jobs by RedirectUrl (avoid duplicates)
                                if (!string.IsNullOrWhiteSpace(job.RedirectUrl) && !allJobs.ContainsKey(job.RedirectUrl))
                                {
                                    allJobs[job.RedirectUrl] = job;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Catch and log any exceptions during the HTTP request or parsing
                        Console.WriteLine($"Error querying Adzuna: {ex.Message}");
                    }
                }
            }

            // Debug output: total unique jobs found after all queries
            Console.WriteLine($"Total unique jobs found: {allJobs.Count}");

            // Return all unique jobs as a list
            return allJobs.Values.ToList();
        }
    }

    // Represents the structure of the Adzuna API JSON response root
    public class AdzunaResponse
    {
        // The list of job results, mapped from "results" JSON property
        [JsonPropertyName("results")]
        public List<JobListing>? Results { get; set; }
    }

    // Represents a single job listing in the Adzuna API response
    public class JobListing
    {
        // Job title mapped from "title"
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        // Company details mapped from "company"
        [JsonPropertyName("company")]
        public Company? Company { get; set; }

        // Location details mapped from "location"
        [JsonPropertyName("location")]
        public Location? Location { get; set; }

        // Job description mapped from "description"
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        // URL to redirect to full job post, mapped from "redirect_url"
        [JsonPropertyName("redirect_url")]
        public string? RedirectUrl { get; set; }
    }

    // Represents company information nested in a job listing
    public class Company
    {
        // Company display name mapped from "display_name"
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    // Represents location information nested in a job listing
    public class Location
    {
        // Location display name mapped from "display_name"
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}
