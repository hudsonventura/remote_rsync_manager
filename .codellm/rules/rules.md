# CodeLLM Rules — Remote Rsync Manager

Please refer to `AGENTS.md` and `ARCHITECTURE.md` in the workspace root for complete architectural and development guidelines.

Key takeaways:
- Backend: .NET 10 Web API + EF Core (Dual SQLite: `server.db` and `logs.db`).
- Sandbox / container note: Use `XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj`.
- Frontend: React 19 + TypeScript + Tailwind CSS + Radix UI + Vite.
- Always verify builds using `XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj` and `npm --prefix src/client run build`.

