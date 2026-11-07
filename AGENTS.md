# Remote Rsync Manager (Remember) — AI Agent Guidelines & Context

Welcome! This document provides complete context, architectural blueprints, coding conventions, and operational workflows for AI assistants (such as Antigravity, Claude, Cursor, Copilot, etc.) working on this repository.

---

## 1. Project Overview & Objective

**Project Name:** Remote Rsync Manager (internal/repo name: `remember` / `remote_rsync_manager`)  
**Author:** Hudson Ventura  
**Primary Objective:**  
A modern web-based graphical interface and orchestrator for centralizing remote backups using `rsync` over SSH from multiple agents/sources.

### Key Philosophy
- **Complements snapshot-enabled storage:** Designed to be used alongside filesystems with native versioning (such as **ZFS**, **TrueNAS Scale**, or Btrfs). Because filesystems like ZFS handle historical snapshots and versioning automatically, Remember focuses on orchestration, agent pairing, scheduling, dry-run simulation, execution, and real-time observability.
- **Agent/Host Model:** Remote machines (Agents) are accessed over SSH using private keys. The Remember Server pulls files via `rsync` into designated storage paths (mapped via `/host` in Docker).
- **Zero-trust / Security-First:** Runs over HTTPS (auto-generates self-signed certificates into `server.pfx`), uses JWT authentication with DB-stored secret keys, and treats SSH keys strictly in-memory or in ephemeral 0600 temporary files during execution.

---

## 2. Tech Stack Summary

### Backend (`src/server`)
- **Framework:** .NET 10 (ASP.NET Core Web API, C# 13 / Implicit Usings / Nullable enabled)
- **Solution format:** Microsoft Visual Studio Solution `.slnx` (`remote_rsync_manager.slnx`)
- **Database & ORM:** SQLite via Entity Framework Core 10 (`Microsoft.EntityFrameworkCore.Sqlite`)
  - **Dual Database Architecture:**
    - `data/server.db`: App configuration, users, agents, backup plans, JWT keys, certificates, Telegram configs.
    - `data/logs.db`: High-volume execution records, rsync transfer logs, and automated log retention cleanup.
- **Real-Time Communication:** ASP.NET Core SignalR (`TerminalHub` connecting to an interactive `/bin/bash` terminal session via `TerminalService`).
- **Scheduling:** `NCrontab` cron expression parser running within the `BackupRunner` hosted background service.
- **Notifications:** Telegram Bot API (`Telegram.Bot` 22.0.0) via `TelegramService` and `TelegramHostedService`.
- **API Documentation:** Scalar API Reference (`Scalar.AspNetCore`) + OpenAPI (`Microsoft.AspNetCore.OpenApi`).

### Frontend (`src/client`)
- **Framework:** React 19 (`react`, `react-dom`)
- **Build Tool:** Vite 7 + TypeScript 5.9
- **Routing:** React Router DOM v7
- **Styling:** Tailwind CSS v3, `tailwindcss-animate`, `class-variance-authority`, `clsx`, `tailwind-merge`
- **UI Components:** Radix UI primitives (`@radix-ui/react-alert-dialog`, `switch`, `label`, `slot`) + Lucide React icons
- **Terminal UI:** `@xterm/xterm` (v5.5) with `@xterm/addon-fit` communicating with SignalR `TerminalHub`
- **Cron Helpers:** `cron-parser` and `cronstrue` for human-readable schedule formatting and validation

### Containerization & Deployment
- **Docker:** Multi-stage build (`Dockerfile`) publishing .NET server + Vite client static assets to `/app/wwwroot`.
- **Orchestration:** `docker-compose.yml` with host volume mounts (`./data` and `/path/your/files/:/host/`).
- **Entrypoint:** `entrypoint.sh` supporting both production serving and client dev mode (`CLIENT_DEV_MODE=true`).

---

## 3. Repository Structure

```
├── .agents/
│   └── rules/                  # Hierarchical rules for Antigravity & LLM agents
├── .codellm/
│   └── rules/                  # Fallback rule directory for CodeLLM/Cursor
├── .vscode/                    # VSCode tasks and launch configurations
├── assets/                     # Screenshots and UI documentation images
├── data/                       # Local SQLite databases (server.db, logs.db) & certificates (ignored by git)
├── HTTPTests/                  # Bruno API test collections
│   ├── Agent/                  # Remote agent pairing/status requests
│   ├── Server/                 # Server login and simulation requests
│   └── environments/           # Bruno environments (dev, prod)
├── src/
│   ├── client/                 # React 19 + TypeScript frontend application
│   │   ├── public/             # Static icons, favicon, logos
│   │   ├── src/
│   │   │   ├── assets/         # Frontend SVG/image assets
│   │   │   ├── components/     # React UI components (Sidebar, Layout, Terminal, FileBrowser, etc.)
│   │   │   │   └── ui/         # Radix UI wrapper components (button, input, switch, etc.)
│   │   │   ├── lib/            # Utilities (`utils.ts`, `api.ts` HTTP helpers)
│   │   │   ├── pages/          # Application views (Dashboard, AgentsList, BackupPlans, Logs, Users, etc.)
│   │   │   ├── App.tsx         # Route definitions and layout wrapper
│   │   │   ├── main.tsx        # React entry point
│   │   │   └── index.css       # Global Tailwind CSS directives
│   │   ├── package.json
│   │   ├── tsconfig.json
│   │   └── vite.config.ts
│   │
│   └── server/                 # ASP.NET Core 10 Web API & Background Services
│       ├── Controllers/        # REST API endpoints (Agent, Auth, BackupPlan, BackupLog, Dashboard, etc.)
│       ├── Data/               # EF Core DbContexts: DBContext (app) and LogDbContext (logs)
│       ├── HostedServices/     # Background workers (BackupRunner, BackupPlanExecutor, LogRetentionService, Telegram)
│       ├── Hubs/               # SignalR Hubs (TerminalHub)
│       ├── Logging/            # CustomLogger & CustomLoggerProvider
│       ├── Migrations/         # EF Core Migrations separated by DbContext (DBContext/ and LogDbContext/)
│       ├── Models/             # Domain entities, DTOs, request/response models
│       ├── Properties/         # launchSettings.json
│       ├── Services/           # Business logic (Auth, JWT, Telegram, Notification, Terminal)
│       ├── Program.cs          # Application startup, DI configuration, middleware pipeline
│       └── server.csproj       # .NET 10 project definition
│
├── docker-compose.yml          # Container orchestration specification
├── Dockerfile                  # Multi-stage Docker build file
├── entrypoint.sh               # Container startup script (supports CLIENT_DEV_MODE)
├── remote_rsync_manager.slnx   # XML-based .NET solution file
├── TELEGRAM_INTEGRATION.md     # Setup guide for Telegram notifications bot
└── README.md                   # User-facing documentation
```

---

## 4. Key Workflows & Business Logic

### A. Backup Plan Scheduling & Execution
1. **Scheduling:** `BackupRunner` executes periodically, queries active `BackupPlan`s from `DBContext`, parses cron expressions via `NCrontab`, and determines if an execution is due.
2. **Execution Flow (`BackupPlanExecutor`):**
   - Retrieves agent SSH details (hostname, user, port, SSH private key).
   - Normalizes SSH private key (CRLF to LF, trim whitespace, trailing newline) and writes to a temporary file (`Path.GetTempPath()`).
   - Sets Unix file permissions to `0600` via `File.SetUnixFileMode` with a fallback to `chmod 600`.
   - **Simulation Mode (Dry-run):** Runs `rsync --dry-run` to detect which files would be transferred, deleted, or ignored without making filesystem changes.
   - **Actual Mode:** Runs `rsync` with progress tracking, parsing stdout/stderr lines in real-time, and recording every transfer/file change into `LogDbContext`.
   - Sends Telegram notification on backup start and backup completion (including duration, files transferred, and status).
   - Deletes temporary SSH key file securely.

### B. Dual Database Separation
- **`DBContext` (`data/server.db`):**
  - Manages `User`, `Agent`, `BackupPlan`, `CertificateConfig`, `JwtConfig`, `AppSettings`, `Notification`, `TelegramConfig`, and `Extension`.
- **`LogDbContext` (`data/logs.db`):**
  - Manages `BackupExecution`, `LogEntry`, and `ExtensionExecution`.
  - Scaled separately so that high logging volume does not degrade or lock primary database transactions.
  - Cleaned up automatically by `LogRetentionService` based on retention policies.

### C. Web Terminal (`TerminalHub` + `TerminalService`)
- Provides an authenticated in-browser bash terminal.
- SignalR connection receives JWT token via query string (`?access_token=...`).
- Spawns `/bin/bash -i -l` process per connection, pipes input/output to xterm.js on the client.

### D. External Extension System (`ExtensionController`, `ExtensionRunner`, `ExtensionExecutor`)
- Allows users to upload external .NET applications as `.zip` packages.
- Archives are safely unpacked into persistent storage under `data/extensions/{id}/`.
- Background scheduler `ExtensionRunner` evaluates cron expressions via `NCrontab` every minute.
- `ExtensionExecutor` spawns the target command (e.g. `dotnet App.dll`), captures exit code and stdout/stderr output.
- Full execution records and output logs are persisted to `LogDbContext` (`logs.db`).

---

## 5. Build, Test, and Run Commands

### Backend (.NET 10)
> [!NOTE]
> In sandboxed environments or Linux containers where `/run/user/<uid>` is not initialized, prefix dotnet commands with `XDG_RUNTIME_DIR=/tmp`.

- **Restore dependencies:**
  ```bash
  XDG_RUNTIME_DIR=/tmp dotnet restore src/server/server.csproj
  ```
- **Build backend:**
  ```bash
  XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj
  # Or build the solution:
  XDG_RUNTIME_DIR=/tmp dotnet build remote_rsync_manager.slnx
  ```
- **Run backend:**
  ```bash
  cd src/server && dotnet run
  ```
- **Entity Framework Migrations:**
  ```bash
  # For DBContext (server.db):
  dotnet ef migrations add <MigrationName> --context DBContext --output-dir Migrations/DBContext

  # For LogDbContext (logs.db):
  dotnet ef migrations add <MigrationName> --context LogDbContext --output-dir Migrations/LogDbContext

  # Apply migrations:
  dotnet ef database update --context DBContext
  dotnet ef database update --context LogDbContext
  ```

### Frontend (React 19 + Vite)
- **Install dependencies:**
  ```bash
  cd src/client && npm install
  ```
- **Run dev server:**
  ```bash
  npm --prefix src/client run dev
  ```
- **Type check and build:**
  ```bash
  npm --prefix src/client run build
  ```
- **Lint:**
  ```bash
  npm --prefix src/client run lint
  ```

### Docker
- **Build and start services:**
  ```bash
  docker compose up -d --build
  ```
- **Follow logs:**
  ```bash
  docker compose logs -f
  ```

---

## 6. Guidelines & Coding Standards for AI Agents

1. **Maintain Codebase Integrity:**
   - Preserve existing architecture: separation of `DBContext` and `LogDbContext`.
   - Never mix application configuration entities into `LogDbContext`.
2. **Security & SSH Keys:**
   - Never persist SSH private keys unencrypted to disk outside of the temporary 0600 file lifecycle in `BackupPlanExecutor`.
   - Ensure proper Unix file permissions when handling keys or certificates.
   - Do not log sensitive credentials (JWT keys, passwords, SSH private keys, Telegram tokens).
3. **C# / .NET Standards:**
   - Use C# 13 features, nullable reference types (`string?`), and asynchronous patterns (`async Task`).
   - Use Dependency Injection (`AddScoped`, `AddSingleton`, `AddHostedService`) appropriately.
   - Favor System.Text.Json (camelCase is configured globally in `Program.cs`).
4. **React / Frontend Standards:**
   - Use modern functional components with TypeScript interfaces.
   - Use Tailwind CSS utility classes; avoid hardcoded inline styles.
   - Use `apiRequest`, `apiGet`, `apiPost`, `apiPut`, `apiDelete` helpers from `@/lib/api.ts` to ensure automatic JWT header injection and 401 handling.
5. **Always Verify Builds:**
   - After modifying C# files, run `XDG_RUNTIME_DIR=/tmp dotnet build src/server/server.csproj` to confirm compilation.
   - After modifying React files, run `npm --prefix src/client run build` to confirm TypeScript compilation.

