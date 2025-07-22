# Stage 1: Build .NET 9 app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY ResumeMatcher.API/*.csproj ./ResumeMatcher.API/
RUN dotnet restore ResumeMatcher.API/ResumeMatcher.API.csproj

# Copy everything else and build
COPY ResumeMatcher.API/. ./ResumeMatcher.API/
RUN dotnet publish ResumeMatcher.API/ResumeMatcher.API.csproj -c Release -o /app/out

# Stage 2: Runtime image with Python
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install Python 3 and pip
RUN apt-get update && apt-get install -y python3 python3-pip

# Copy published .NET app
COPY --from=build /app/out .

# Copy Python scripts and requirements
COPY ResumeMatcher.API/Python/ ./Python/

# Install Python dependencies
RUN pip3 install --upgrade pip
RUN pip3 install -r Python/requirements.txt

EXPOSE 80

ENTRYPOINT ["dotnet", "ResumeMatcher.API.dll"]
