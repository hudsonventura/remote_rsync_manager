# Antigravity Rules — Remote Rsync Manager (Remember)

## Code Quality & Architecture Rules

1. **Target Frameworks & Versions:**
   - Server: .NET 10 (`net10.0`), C# 13, ImplicitUsings enabled, Nullable enabled.
   - Client: React 19, TypeScript 5.9, Vite 7, Tailwind CSS v3.

2. **Dual-Database Segregation:**
   - Any new entity related to app configuration, authentication, agents, backup definitions, or integrations belongs in `server.Data.DBContext` and maps to `data/server.db`.
   - Any high-volume telemetry, logs, or file execution history belongs in `server.Data.LogDbContext` and maps to `data/logs.db`.
   - Never cross-reference entity navigation properties across different DbContexts directly.

3. **Linux / Sandbox Command Execution:**
   - When executing `dotnet` commands in environments where `/run/user/<uid>` is not mounted, always prepend `XDG_RUNTIME_DIR=/tmp` (e.g., `XDG_RUNTIME_DIR=/tmp dotnet build ...`).

4. **SSH & Security Integrity:**
   - Private SSH keys must never be logged or stored unencrypted permanently.
   - When generating temporary key files for `rsync` execution, enforce Unix permissions `0600` via `File.SetUnixFileMode` and `chmod 600`.
   - Ensure temporary key files are deleted in `finally` blocks.

5. **Frontend Conventions:**
   - Use Tailwind CSS for all layout and styling.
   - Use Radix UI primitives (`@/components/ui/*`) and Lucide React icons.
   - Ensure all API calls route through `@/lib/api.ts` to maintain consistent Bearer token attachment and automatic redirection on 401 Unauthorized responses.

