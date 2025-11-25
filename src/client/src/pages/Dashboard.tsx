import { useEffect, useState } from "react"
import { apiGet } from "@/lib/api"

interface DashboardStats {
  activeExecutions: number
  totalFilesCopied: number
  totalSizeCopied: number
  totalAgents: number
  totalBackupPlans: number
}

function formatFileSize(bytes: number | null): string {
  if (bytes === null || bytes === undefined) return "N/A"
  if (bytes === 0) return "0 B"

  const k = 1024
  const sizes = ["B", "KB", "MB", "GB", "TB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

export function Dashboard() {
  const [stats, setStats] = useState<DashboardStats | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchStats = async () => {
      setIsLoading(true)
      setError(null)

      try {
        const data = await apiGet<DashboardStats>("/api/dashboard/stats")
        setStats(data)
      } catch (err) {
        if (err instanceof TypeError && err.message === "Failed to fetch") {
          setError("Unable to connect to the server. Please make sure the backend is running.")
        } else {
          setError(err instanceof Error ? err.message : "An error occurred while loading dashboard statistics")
        }
      } finally {
        setIsLoading(false)
      }
    }

    fetchStats()

    // Refresh stats every 30 seconds
    const interval = setInterval(fetchStats, 30000)

    return () => clearInterval(interval)
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Dashboard</h1>
        <p className="text-muted-foreground mt-2">
          Overview of your activities and statistics
        </p>
      </div>

      {error && (
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {isLoading ? (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {[1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="rounded-lg border bg-card p-6 shadow-sm">
              <h3 className="text-sm font-medium text-muted-foreground">Loading...</h3>
              <p className="text-2xl font-bold mt-2">-</p>
            </div>
          ))}
        </div>
      ) : stats ? (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          <div className="rounded-lg border bg-card p-6 shadow-sm">
            <h3 className="text-sm font-medium text-muted-foreground">Backup Plans in Execution</h3>
            <p className="text-2xl font-bold mt-2">{stats.activeExecutions}</p>
            <p className="text-xs text-muted-foreground mt-1">Currently running</p>
          </div>
          <div className="rounded-lg border bg-card p-6 shadow-sm">
            <h3 className="text-sm font-medium text-muted-foreground">Files Copied</h3>
            <p className="text-2xl font-bold mt-2">{stats.totalFilesCopied.toLocaleString()}</p>
            <p className="text-xs text-muted-foreground mt-1">Total files copied</p>
          </div>
          <div className="rounded-lg border bg-card p-6 shadow-sm">
            <h3 className="text-sm font-medium text-muted-foreground">Data Copied</h3>
            <p className="text-2xl font-bold mt-2">{formatFileSize(stats.totalSizeCopied)}</p>
            <p className="text-xs text-muted-foreground mt-1">Total size copied</p>
          </div>
          <div className="rounded-lg border bg-card p-6 shadow-sm">
            <h3 className="text-sm font-medium text-muted-foreground">Total Agents</h3>
            <p className="text-2xl font-bold mt-2">{stats.totalAgents}</p>
            <p className="text-xs text-muted-foreground mt-1">Registered agents</p>
          </div>
          <div className="rounded-lg border bg-card p-6 shadow-sm">
            <h3 className="text-sm font-medium text-muted-foreground">Total Backup Plans</h3>
            <p className="text-2xl font-bold mt-2">{stats.totalBackupPlans}</p>
            <p className="text-xs text-muted-foreground mt-1">Registered backup plans</p>
          </div>
        </div>
      ) : null}
    </div>
  )
}

