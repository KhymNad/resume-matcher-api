# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY ResumeMatcher.API/*.csproj ./ResumeMatcher.API/
RUN dotnet restore ResumeMatcher.API/ResumeMatcher.API.csproj

# Copy everything else and build
COPY ResumeMatcher.API/. ./ResumeMatcher.API/
RUN dotnet publish ResumeMatcher.API/ResumeMatcher.API.csproj -c Release -o /app/out

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

EXPOSE 80

ENTRYPOINT ["dotnet", "ResumeMatcher.API.dll"]
