# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["server.csproj", "./"]
RUN dotnet restore "server.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "server.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create directory for databases
RUN mkdir -p /app/data

# Copy published app
COPY --from=publish /app/publish .

# Expose ports
EXPOSE 5000
EXPOSE 5001

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

# Set working directory for databases
ENV DataDirectory=/app/data

# Run the application
ENTRYPOINT ["dotnet", "server.dll"]

