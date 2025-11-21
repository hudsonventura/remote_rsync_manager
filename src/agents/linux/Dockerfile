# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["linux/agent.csproj", "linux/"]
COPY ["../common/common.csproj", "common/"]
RUN dotnet restore "linux/agent.csproj"

# Copy everything else and build
COPY ["linux/", "linux/"]
COPY ["../common/", "common/"]
RUN dotnet build "linux/agent.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "linux/agent.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create directory for database
RUN mkdir -p /app/data

# Copy published app
COPY --from=publish /app/publish .

# Expose ports (default agent port)
EXPOSE 5002

# Set environment variables
ENV ASPNETCORE_URLS=https://+:5002
ENV ASPNETCORE_ENVIRONMENT=Production

# Set working directory for database
ENV DataDirectory=/app/data

# Run the application
ENTRYPOINT ["dotnet", "agent.dll"]

