# Build stage for .NET server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/server/server.csproj", "src/server/"]
RUN dotnet restore "src/server/server.csproj"

# Copy server source and build
COPY src/server/ ./src/server/
RUN dotnet build "src/server/server.csproj" -c Release -o /app/build

# Publish server
FROM server-build AS server-publish
RUN dotnet publish "src/server/server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build stage for Node.js client
FROM node:20-alpine AS client-build
WORKDIR /app

# Copy client package files
COPY src/client/package*.json ./
RUN npm ci

# Copy client source and build
COPY src/client/ .
RUN npm run build

# Runtime stage - use SDK image to have Node.js available for dev mode
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Node.js for running client dev server in development mode
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Create directory for databases and static files
RUN mkdir -p /app/data /app/wwwroot

# Copy published server
COPY --from=server-publish /app/publish .

# Copy built client files to wwwroot
COPY --from=client-build /app/dist ./wwwroot

# Copy client source for development mode (if needed)
COPY src/client /app/src/client

# Expose ports
EXPOSE 5000
EXPOSE 5001

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

# Set working directory for databases
ENV DataDirectory=/app/data

# Copy entrypoint script
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Run the application
ENTRYPOINT ["/app/entrypoint.sh"]

