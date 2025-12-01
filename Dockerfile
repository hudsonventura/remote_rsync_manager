# =====================================================
# 1. Build stage for .NET server
# =====================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src

COPY ["src/server/server.csproj", "src/server/"]
RUN dotnet restore "src/server/server.csproj"

COPY src/server/ ./src/server/
RUN dotnet build "src/server/server.csproj" -c Release -o /app/build

FROM server-build AS server-publish
RUN dotnet publish "src/server/server.csproj" -c Release -o /app/publish /p:UseAppHost=false


# =====================================================
# 2. Build stage for frontend (Vite)
# =====================================================
FROM node:20-alpine AS client-build
WORKDIR /app

COPY src/client/package*.json ./
RUN npm ci

COPY src/client/ .
RUN npm run build


# =====================================================
# 3. Final runtime image
# =====================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install Node.js so dev-mode can run inside container
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Folders for DB + static files
RUN mkdir -p /app/data /app/wwwroot

# Copy server publish
COPY --from=server-publish /app/publish .

# Copy built client (prod)
COPY --from=client-build /app/dist ./wwwroot

# Copy raw client source *without node_modules* for dev-mode
COPY src/client /app/src/client

# Fix Windows CRLF (common cause of "no such file or directory")
RUN sed -i 's/\r$//' /app/src/client/*.js || true
RUN sed -i 's/\r$//' /app/entrypoint.sh || true

# Environment
ENV ASPNETCORE_URLS="http://+:5000 https://+:5001"
ENV ASPNETCORE_ENVIRONMENT=Development
ENV DataDirectory=/app/data

EXPOSE 5000
EXPOSE 5001


# Entrypoint
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
