using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResumeMatcherAPI.Services;
using ResumeMatcherAPI.Helpers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Npgsql;
using Pgvector;

// Load .env file if it exists (skip in testing environment)
try { DotNetEnv.Env.Load(); } catch { /* .env file may not exist in test environment */ }

var builder = WebApplication.CreateBuilder(args);

// Register additional encodings (fixes 'utf8' bug)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Register Services
builder.Services.AddControllers();
builder.Services.AddHttpClient<HuggingFaceNlpService>();
builder.Services.AddSingleton<IFileTextExtractor, FileTextExtractor>();
builder.Services.AddHttpClient<AdzunaJobService>();
builder.Services.AddSingleton<AdzunaJobService>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<SkillService>();
builder.Services.AddHttpClient<PythonResumeParserService>();

// Swagger for API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS policy to allow frontend (Vercel & local dev)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000",     // React dev server (Vite uses 5173)
            "http://localhost:5173",     // Vite
            "https://resume-matcher-client.vercel.app" // Vercel 
        )
                .AllowAnyHeader()
                .AllowAnyMethod();
    });
});

// Configure EmbeddingHelper with Hugging Face settings
EmbeddingHelper.Configure(builder.Configuration);


NpgsqlConnection.GlobalTypeMapper.UseVector();

// Build the app
var app = builder.Build();

// Configure Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();

// Expose the Program class for WebApplicationFactory in integration tests
public partial class Program { }
