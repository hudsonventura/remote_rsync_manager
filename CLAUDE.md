# Remote Rsync Manager (Remember) — Claude Developer Guide

## Quick Reference Commands

### Build & Run Backend (.NET 10)
> **Note:** If running in containerized or restricted Linux environments, set `XDG_RUNTIME_DIR=/tmp` to prevent NuGet mutex restore errors.

- **Restore packages:** `XDG_RUNTIME_DIR=/tmp dotnet restore src/server/server.csproj`
- **Build backend:** `XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj`
- **Build solution:** `XDG_RUNTIME_DIR=/tmp dotnet build remote_rsync_manager.slnx`
- **Run server:** `cd src/server && dotnet run`

### EF Core Migrations
- **Main DB (`server.db`):**
  `dotnet ef migrations add <Name> --context DBContext --output-dir Migrations/DBContext`
- **Logs DB (`logs.db`):**
  `dotnet ef migrations add <Name> --context LogDbContext --output-dir Migrations/LogDbContext`
- **Update DB:**
  `dotnet ef database update --context DBContext`

### Build & Run Frontend (React 19 + Vite)
- **Install deps:** `npm --prefix src/client install`
- **Dev server:** `npm --prefix src/client run dev`
- **Type-check & build:** `npm --prefix src/client run build`
- **Lint:** `npm --prefix src/client run lint`

### Docker Deployment
- **Start stack:** `docker compose up -d`
- **Rebuild & start:** `docker compose up -d --build`
- **View logs:** `docker compose logs -f`

---

## Architectural Rules & Key Conventions

1. **Dual SQLite Architecture:**
   - Always keep `DBContext` (for app configuration, users, agents, backup plans) and `LogDbContext` (for executions and transfer logs) strictly segregated.
2. **SSH Key Safety:**
   - Normalize keys (LF line endings, trailing newline) before saving to `/tmp`.
   - Maintain `0600` permissions on temporary key files and ensure they are removed in a `finally` block.
3. **SignalR Web Terminal:**
   - `TerminalHub` maps to `/hubs/terminal` and requires JWT authentication (`access_token` query param support).
4. **API Requests:**
   - In the React frontend, always use helper methods from `@/lib/api.ts` (`apiGet`, `apiPost`, `apiPut`, `apiDelete`) for automatic JWT token injection and 401 handling.
5. **Always verify compilation:**
   - Execute `XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj` and `npm --prefix src/client run build` after making modifications.

