namespace ResumeMatcher.Tests.Integration.Fixtures;

/// <summary>
/// Static test data fixtures for consistent, reusable test data.
/// Provides sample resumes, expected NER responses, and job listings.
/// </summary>
public static class TestDataFixtures
{
    #region Sample Resume Texts

    public static class Resumes
    {
        public static string SoftwareEngineer => @"
John Smith
Senior Software Engineer
john.smith@email.com | (555) 123-4567 | San Francisco, CA | linkedin.com/in/johnsmith

PROFESSIONAL SUMMARY
Experienced software engineer with 8+ years of experience in full-stack development,
specializing in Python, JavaScript, and cloud technologies. Proven track record of
leading teams and delivering scalable solutions.

EXPERIENCE

Senior Software Engineer | Google | Mountain View, CA | 2020 - Present
- Architected and implemented microservices using Python and Go
- Led migration of legacy systems to Kubernetes, reducing infrastructure costs by 40%
- Mentored team of 5 junior engineers and conducted code reviews
- Implemented CI/CD pipelines using GitHub Actions and ArgoCD

Software Engineer | Meta | Menlo Park, CA | 2017 - 2020
- Developed React frontend applications serving 10M+ users
- Built GraphQL APIs using Node.js and TypeScript
- Optimized database queries reducing response time by 60%
- Collaborated with product team to define technical requirements

Junior Developer | Startup Inc | San Francisco, CA | 2015 - 2017
- Built RESTful APIs using Python Flask
- Implemented automated testing using pytest
- Managed PostgreSQL and Redis databases

EDUCATION
Master of Science in Computer Science
Stanford University | 2015

Bachelor of Science in Computer Engineering
UC Berkeley | 2013

SKILLS
Languages: Python, JavaScript, TypeScript, Go, SQL
Frameworks: React, Node.js, Flask, Django, FastAPI
Cloud: AWS, GCP, Kubernetes, Docker, Terraform
Databases: PostgreSQL, MongoDB, Redis, Elasticsearch
Tools: Git, GitHub Actions, Jenkins, Datadog, Grafana
";

        public static string DataScientist => @"
Dr. Emily Chen
Data Scientist
emily.chen@email.com | Boston, MA

SUMMARY
PhD Data Scientist with expertise in machine learning, statistical modeling, and data analysis.
5+ years of experience applying ML to solve business problems at scale.

EXPERIENCE

Senior Data Scientist | Amazon | Seattle, WA | 2021 - Present
- Developed recommendation algorithms serving 100M+ customers
- Built and deployed ML models using PyTorch and SageMaker
- Led A/B testing initiatives to optimize conversion rates
- Created data pipelines using Apache Spark and Airflow

Data Scientist | Microsoft | Redmond, WA | 2019 - 2021
- Implemented NLP models for text classification
- Built predictive models using scikit-learn and XGBoost
- Conducted statistical analysis using R and Python
- Collaborated with engineering to productionize models

Research Assistant | MIT | Cambridge, MA | 2016 - 2019
- Published 3 papers in top ML conferences
- Developed deep learning models using TensorFlow

EDUCATION
Ph.D. in Computer Science (Machine Learning)
Massachusetts Institute of Technology | 2019

Bachelor of Science in Mathematics
Harvard University | 2016

SKILLS
Python, R, SQL, PyTorch, TensorFlow, scikit-learn, Spark, AWS, Machine Learning, Deep Learning, NLP, Statistics
";

        public static string FrontendDeveloper => @"
Alex Rivera
Frontend Developer
alex.rivera@gmail.com | New York, NY

EXPERIENCE

Frontend Engineer | Spotify | New York, NY | 2022 - Present
- Built responsive web applications using React and TypeScript
- Implemented state management with Redux and React Query
- Optimized Core Web Vitals scores improving performance by 35%
- Developed component library using Storybook

Junior Frontend Developer | Startup | Brooklyn, NY | 2020 - 2022
- Created single-page applications using Vue.js
- Implemented responsive designs with CSS and Tailwind
- Built REST API integrations using Axios

EDUCATION
Bachelor of Arts in Computer Science
New York University | 2020

SKILLS
JavaScript, TypeScript, React, Vue.js, HTML, CSS, Tailwind, Redux, Next.js, Webpack
";

        public static string MinimalResume => @"
Jane Doe
jane.doe@email.com

Looking for software development opportunities.
";

        public static string ResumeWithUnicodeCharacters => @"
José García
Développeur Senior
jose.garcia@email.com | São Paulo, Brazil

EXPERIENCE
Développeur Senior | Compañía Tech | 2020 - Present
- Développement d'applications avec Python et JavaScript
- Expérience avec AWS et Docker

ÉDUCATION
Université de São Paulo | 2018
Ingénieur en Informatique

COMPÉTENCES
Python, JavaScript, Docker, AWS, Français, Español, Português
";
    }

    #endregion

    #region Expected NER Responses

    public static class NerResponses
    {
        public static object[] SoftwareEngineerEntities => new object[]
        {
            new { entity_group = "PER", word = "John", score = 0.95f, start = 1, end = 5 },
            new { entity_group = "PER", word = "Smith", score = 0.93f, start = 6, end = 11 },
            new { entity_group = "LOC", word = "San Francisco", score = 0.89f, start = 80, end = 93 },
            new { entity_group = "ORG", word = "Google", score = 0.92f, start = 300, end = 306 },
            new { entity_group = "ORG", word = "Meta", score = 0.91f, start = 600, end = 604 },
            new { entity_group = "ORG", word = "Stanford University", score = 0.88f, start = 1000, end = 1019 }
        };

        public static object[] DataScientistEntities => new object[]
        {
            new { entity_group = "PER", word = "Emily", score = 0.94f, start = 4, end = 9 },
            new { entity_group = "PER", word = "Chen", score = 0.92f, start = 10, end = 14 },
            new { entity_group = "LOC", word = "Boston", score = 0.87f, start = 45, end = 51 },
            new { entity_group = "ORG", word = "Amazon", score = 0.91f, start = 200, end = 206 },
            new { entity_group = "ORG", word = "MIT", score = 0.89f, start = 800, end = 803 }
        };

        public static object[] EmptyResponse => Array.Empty<object>();
    }

    #endregion

    #region Job Listing Responses

    public static class JobResponses
    {
        public static object SoftwareEngineerJobs => new
        {
            results = new[]
            {
                new
                {
                    title = "Senior Software Engineer",
                    company = new { display_name = "Google" },
                    location = new { display_name = "Mountain View, CA" },
                    description = "Looking for experienced software engineers to join our team. Python and Go experience required.",
                    redirect_url = "https://careers.google.com/jobs/123"
                },
                new
                {
                    title = "Full Stack Developer",
                    company = new { display_name = "Meta" },
                    location = new { display_name = "Menlo Park, CA" },
                    description = "Build products that connect people. React and Node.js experience preferred.",
                    redirect_url = "https://careers.meta.com/jobs/456"
                },
                new
                {
                    title = "Backend Engineer",
                    company = new { display_name = "Netflix" },
                    location = new { display_name = "Los Gatos, CA" },
                    description = "Join our streaming platform team. Java or Python required.",
                    redirect_url = "https://jobs.netflix.com/jobs/789"
                }
            }
        };

        public static object DataScienceJobs => new
        {
            results = new[]
            {
                new
                {
                    title = "Senior Data Scientist",
                    company = new { display_name = "Amazon" },
                    location = new { display_name = "Seattle, WA" },
                    description = "Apply ML to solve complex problems at scale. Python and ML experience required.",
                    redirect_url = "https://amazon.jobs/en/jobs/123"
                },
                new
                {
                    title = "Machine Learning Engineer",
                    company = new { display_name = "OpenAI" },
                    location = new { display_name = "San Francisco, CA" },
                    description = "Work on cutting-edge AI research. PyTorch experience preferred.",
                    redirect_url = "https://openai.com/careers/456"
                }
            }
        };

        public static object EmptyJobResponse => new { results = Array.Empty<object>() };
    }

    #endregion

    #region Skill Data

    public static class Skills
    {
        public static string[] ProgrammingLanguages => new[]
        {
            "Python", "JavaScript", "TypeScript", "Java", "C#", "C++", "Go", "Rust", "Ruby", "PHP", "Swift", "Kotlin", "R", "SQL"
        };

        public static string[] Frameworks => new[]
        {
            "React", "Angular", "Vue.js", "Node.js", "Django", "Flask", "FastAPI", "Spring Boot", ".NET", "Rails", "Next.js", "Express.js"
        };

        public static string[] CloudAndDevOps => new[]
        {
            "AWS", "Azure", "GCP", "Docker", "Kubernetes", "Terraform", "Jenkins", "GitHub Actions", "CircleCI", "Ansible"
        };

        public static string[] Databases => new[]
        {
            "PostgreSQL", "MySQL", "MongoDB", "Redis", "Elasticsearch", "DynamoDB", "Cassandra", "SQLite"
        };

        public static string[] MachineLearning => new[]
        {
            "Machine Learning", "Deep Learning", "TensorFlow", "PyTorch", "scikit-learn", "NLP", "Computer Vision", "Neural Networks"
        };

        public static Skill[] CreateSkillEntities()
        {
            var allSkills = ProgrammingLanguages
                .Select(s => new Skill { Id = Guid.NewGuid(), Name = s, Type = "Programming", Source = "fixture" })
                .Concat(Frameworks.Select(s => new Skill { Id = Guid.NewGuid(), Name = s, Type = "Framework", Source = "fixture" }))
                .Concat(CloudAndDevOps.Select(s => new Skill { Id = Guid.NewGuid(), Name = s, Type = "DevOps", Source = "fixture" }))
                .Concat(Databases.Select(s => new Skill { Id = Guid.NewGuid(), Name = s, Type = "Database", Source = "fixture" }))
                .Concat(MachineLearning.Select(s => new Skill { Id = Guid.NewGuid(), Name = s, Type = "ML", Source = "fixture" }))
                .ToArray();

            return allSkills;
        }
    }

    #endregion
}
