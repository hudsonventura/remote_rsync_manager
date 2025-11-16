# Remember Backup System

A comprehensive backup solution for managing and automating file backups across multiple agents.

## Features

- Multi-agent backup management with secure pairing
- Flexible cron-based scheduling with human-readable descriptions
- Backup simulation to preview changes before execution
- Manual backup execution on demand
- Detailed logging with filtering and sorting capabilities
- File system browsing for both remote agents and local server
- Active/inactive backup plan management
- Intelligent file comparison (by name and size) to minimize unnecessary transfers

## Technology Stack

### Backend
- ASP.NET Core 10.0 (C#)
- Entity Framework Core
- SQLite Database
- JWT Authentication
- NCrontab for scheduling

### Frontend
- React with TypeScript
- Vite
- Tailwind CSS
- Shadcn UI Components
- React Router

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Node.js and npm
- SQLite (included with .NET)

### Important: Certificate Setup

**Before accessing the frontend, you must first accept the self-signed SSL certificate:**

1. Open your browser and navigate to: **https://localhost:5001/login**
2. You will see a security warning about the certificate
3. Click "Advanced" or "Show Details"
4. Click "Proceed to localhost" or "Accept the Risk and Continue"
5. Once the certificate is accepted, you can access the frontend normally

This step is required because the backend uses a self-signed certificate for HTTPS, and browsers need to trust it before making API requests.

### Running the Application

#### Option 1: Using Docker (Recommended for Production)

1. **Build and start all services:**
   ```bash
   docker-compose up -d
   ```

2. **View logs:**
   ```bash
   docker-compose logs -f
   ```

3. **Stop services:**
   ```bash
   docker-compose down
   ```

4. **Access the application:**
   - Backend API: https://localhost:5001
   - Agent: https://localhost:5002
   - API Documentation: https://localhost:5001/scalar/v1

**Note:** The agent container mounts the host filesystem at `/host` (read-only) for backup operations. Adjust the volume mount in `docker-compose.yml` as needed.

#### Option 2: Manual Development Setup

1. **Start the backend server:**
   ```bash
   cd src/server
   dotnet run
   ```
   The server will start on `https://localhost:5001` and `http://localhost:5000`

2. **Start the frontend development server:**
   ```bash
   cd src/client
   npm install
   npm run dev
   ```
   The frontend will be available at `http://localhost:5173`

3. **Start the agent (optional, for testing):**
   ```bash
   cd src/agents/linux
   dotnet run
   ```
   The agent will start on `https://localhost:5002`

4. **Access the application:**
   - Frontend: http://localhost:5173
   - Backend API: https://localhost:5001
   - Agent: https://localhost:5002
   - API Documentation: https://localhost:5001/scalar/v1

### Default Credentials

The application uses JWT authentication. You'll need to create an account or use the authentication endpoints to register/login.

## Development

### VS Code Setup

The project includes VS Code tasks and launch configurations:
- Use the "dotnet: build" task to build the server
- Use the "Server" launch configuration to debug the backend
- The client and agent will automatically start/stop with the server debug session

### Database

The application uses SQLite databases:
- `remember.db` - Main database (agents, backup plans, users)
- `logs.db` - Backup operation logs

Migrations are applied automatically on startup.

## Project Structure

```
remember/
├── src/
│   ├── server/          # ASP.NET Core backend
│   ├── client/          # React frontend
│   └── agents/          # Agent applications
│       └── linux/       # Linux agent
└── .vscode/             # VS Code configuration
```

## License

This project is licensed under the MIT License.

## Author

Developed by [Hudson Ventura](https://github.com/hudsonventura)

