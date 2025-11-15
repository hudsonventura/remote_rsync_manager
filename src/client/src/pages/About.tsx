import { Button } from "@/components/ui/button"
import { Github, Database, Server, Shield, Clock, FileText } from "lucide-react"

export function About() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">About Remember Backup System</h1>
        <p className="text-muted-foreground mt-2">
          A comprehensive backup solution for managing and automating file backups across multiple agents
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <Server className="h-6 w-6 text-primary" />
            <h2 className="text-xl font-semibold">Agent-Based Architecture</h2>
          </div>
          <p className="text-muted-foreground">
            Deploy lightweight agents on remote systems to enable secure, distributed backup operations. 
            Each agent can be paired with the server using secure token authentication.
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <Clock className="h-6 w-6 text-primary" />
            <h2 className="text-xl font-semibold">Cron-Based Scheduling</h2>
          </div>
          <p className="text-muted-foreground">
            Schedule backups using standard cron expressions. The system automatically executes backup 
            plans at the specified intervals, ensuring your data is backed up regularly.
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <Database className="h-6 w-6 text-primary" />
            <h2 className="text-xl font-semibold">Comprehensive Logging</h2>
          </div>
          <p className="text-muted-foreground">
            Every backup operation is logged with detailed information including file names, sizes, 
            actions (copy/delete/ignore), and reasons. Filter and sort logs to track backup history.
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <Shield className="h-6 w-6 text-primary" />
            <h2 className="text-xl font-semibold">Secure & Reliable</h2>
          </div>
          <p className="text-muted-foreground">
            Secure agent authentication using tokens, JWT-based user authentication, and encrypted 
            communication. Built with reliability and data integrity in mind.
          </p>
        </div>
      </div>

      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <h2 className="text-xl font-semibold mb-4">Features</h2>
        <ul className="space-y-2 text-muted-foreground">
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Multi-agent backup management with secure pairing</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Flexible cron-based scheduling with human-readable descriptions</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Backup simulation to preview changes before execution</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Manual backup execution on demand</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Detailed logging with filtering and sorting capabilities</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>File system browsing for both remote agents and local server</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Active/inactive backup plan management</span>
          </li>
          <li className="flex items-start gap-2">
            <span className="text-primary mt-1">•</span>
            <span>Intelligent file comparison (by name and size) to minimize unnecessary transfers</span>
          </li>
        </ul>
      </div>

      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <h2 className="text-xl font-semibold mb-4">Technology Stack</h2>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <h3 className="font-medium mb-2">Backend</h3>
            <ul className="space-y-1 text-sm text-muted-foreground">
              <li>• ASP.NET Core (C#)</li>
              <li>• Entity Framework Core</li>
              <li>• SQLite Database</li>
              <li>• JWT Authentication</li>
              <li>• NCrontab for scheduling</li>
            </ul>
          </div>
          <div>
            <h3 className="font-medium mb-2">Frontend</h3>
            <ul className="space-y-1 text-sm text-muted-foreground">
              <li>• React with TypeScript</li>
              <li>• Vite</li>
              <li>• Tailwind CSS</li>
              <li>• Shadcn UI Components</li>
              <li>• React Router</li>
            </ul>
          </div>
        </div>
      </div>

      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-xl font-semibold mb-2">Developer</h2>
            <p className="text-muted-foreground mb-4">
              Remember Backup System is developed and maintained by Hudson Ventura
            </p>
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <FileText className="h-4 w-4" />
              <span>Full Stack Developer | C# | React | PostgreSQL | GNU Linux</span>
            </div>
          </div>
          <Button
            variant="outline"
            onClick={() => window.open("https://github.com/hudsonventura", "_blank")}
            className="flex items-center gap-2"
          >
            <Github className="h-4 w-4" />
            View GitHub Profile
          </Button>
        </div>
      </div>
    </div>
  )
}

