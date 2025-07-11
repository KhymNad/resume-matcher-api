using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ResumeMatcherAPI.Services
{
    /// <summary>
    /// Provides methods to interact with the 'skills' data stored in the database.
    /// </summary>
    public class SkillService
    {
        // Connection string to the PostgreSQL database
        private readonly string? _connectionString;

        /// <summary>
        /// Initializes a new instance of SkillService using configuration to get the connection string.
        /// </summary>
        /// <param name="configuration">Application configuration to access connection strings.</param>
        public SkillService(IConfiguration configuration)
        {
            // Retrieves the connection string
            _connectionString = configuration.GetConnectionString("Supabase");
        }

        /// <summary>
        /// Asynchronously retrieves all skill names from the 'skills' table in the database.
        /// </summary>
        /// <returns>A list of skill names as strings.</returns>
        public async Task<List<string>> GetAllSkillsAsync()
        {
            var skills = new List<string>();

            // Create and open a connection to the PostgreSQL database asynchronously
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Prepare the SQL command to select all skill names
            var cmd = new NpgsqlCommand("SELECT name FROM skills", conn);

            // Execute the command and read results asynchronously
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // Add each skill name to the list
                skills.Add(reader.GetString(0));
            }

            // Return the list of skill names
            return skills;
        }
    }
}
