using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResumeMatcherAPI.Services;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// Register additional encodings (fixes 'utf8' bug)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Register Services
builder.Services.AddControllers();
builder.Services.AddHttpClient<HuggingFaceNlpService>();
builder.Services.AddSingleton<FileTextExtractor>();
builder.Services.AddHttpClient<AdzunaJobService>();
builder.Services.AddSingleton<AdzunaJobService>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<SkillService>();


// Swagger for API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS policy to allow frontend (Vercel & local dev)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000") // TODO: add vercel domain for frontend 
                .AllowAnyHeader()
                .AllowAnyMethod();
    });
});

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
