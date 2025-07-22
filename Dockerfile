# Stage 1: Build .NET 9 app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY ResumeMatcher.API/*.csproj ./ResumeMatcher.API/
RUN dotnet restore ResumeMatcher.API/ResumeMatcher.API.csproj

# Copy everything else and build
COPY ResumeMatcher.API/. ./ResumeMatcher.API/
RUN dotnet publish ResumeMatcher.API/ResumeMatcher.API.csproj -c Release -o /app/out

# Stage 2: Runtime image with Python and venv
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install Python 3, venv, and pip
RUN apt-get update && apt-get install -y python3 python3-venv python3-pip

# Create virtual environment
RUN python3 -m venv /opt/venv

# Upgrade pip inside the virtual environment
RUN /opt/venv/bin/pip install --upgrade pip

# Copy published .NET app
COPY --from=build /app/out .

# Copy Python scripts and requirements file
COPY ResumeMatcher.API/Python/ ./Python/

# Install Python dependencies inside the virtual environment
RUN /opt/venv/bin/pip install -r Python/requirements.txt

EXPOSE 80

# Use your .NET app entry point as usual
ENTRYPOINT ["dotnet", "ResumeMatcher.API.dll"]
