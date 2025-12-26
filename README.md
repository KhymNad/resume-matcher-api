# ResumeMatcherAPI

**ResumeMatcherAPI** is a full-stack AI-driven resume processing and job matching backend built using **ASP.NET Core** and **Python**. It extracts and parses resume text using a Python script, identifies key entities (skills, experience, education) via Hugging Face NER, and fetches real-time job postings using the Adzuna API.

---

## Features

- Upload and parse resumes (PDF format)
- NLP-powered entity extraction using Hugging Face
- Python scripting for resume text extraction
- Fetch relevant job postings from Adzuna based on parsed skills
- Deployable to Render (multi-service architecture)

---

## Project Structure
```bash
ResumeMatcher.API/
├── Controllers/
│ └── ResumeController.cs
│ └── SupabaseController.cs
├── Services/
│ ├── AdzunaJobService.cs
│ ├── ApplicationDBContext.cs
│ ├── FileTextExtractor.cs
│ └── HuggingFaceNlpService.cs
│ └── SkillMatcher.cs
├── Helpers/
│ └── ResumeControllerHelpers.cs
├── Python/
│ ├── parse_resume_script.py # internal Python script
│ └── requirements.txt
├── appsettings.json
├── Program.cs
├── .env
└── ResumeMatcher.API.csproj
.render.yaml
Dockerfile
```

---

## Prerequisites

- [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [Python 3.9+](https://www.python.org/downloads/)
- Supabase account with PostgreSQL database (and optional `pgvector`)
- Hugging Face account (for API key)
- Adzuna developer account (App ID & Key)

---

## Environment Variables

Create a `.env` file at the root of the .NET project or use Render’s Secret Environment system:

```bash
HUGGINGFACE_API_KEY=your_huggingface_token
ADZUNA_APP_ID=your_adzuna_app_id
ADZUNA_APP_KEY=your_adzuna_app_key
PYTHON_PARSER_URL=http://localhost:5001 # Or https://<render-url> if deployed
SUPABASE_CONNECTION_STRING=postgresql://user:password@host:port/database
```

---

## Start the .NET Web API (Locally)

```bash
cd ResumeMatcherAPI
dotnet restore
dotnet run
```

---

## API Endpoints
Method	Endpoint	Description

GET	/api/resume/health	Health check

GET	/api/resume/test-huggingface	Sends sample text to Hugging Face NER

POST	/api/resume/upload	Upload resume, extract + group entities

POST	/api/resume/upload-with-jobs	Upload resume + return job matches (Adzuna)

---

## Dependencies
ASP.NET Core 6

Hugging Face Transformers API

Adzuna API

Supabase (PostgreSQL + pgvector)

Python 3.9

PDFPlumber (PDF parsing)

Docker (for deployment)
