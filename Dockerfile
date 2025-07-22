# 1. Build your .NET app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything and restore + publish
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# 2. Create the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install Python 3 and pip
RUN apt-get update && apt-get install -y python3 python3-pip

# Copy published .NET app
COPY --from=build /app/publish .

# Copy your Python folder and requirements.txt
COPY Python/ ./Python/

# Install Python dependencies
RUN pip3 install --upgrade pip
RUN pip3 install -r Python/requirements.txt

# Expose the port your .NET app listens on (default 80)
EXPOSE 80

# Start the .NET app
ENTRYPOINT ["dotnet", "ResumeMatcher.API.dll"]
