import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { ArrowLeft, ChevronLeft, ChevronRight } from "lucide-react"
import { apiGet } from "@/lib/api"

interface LogEntry {
  id: string
  dateTime: string
  fileName: string
  filePath: string
  size: number | null
  action: string
  reason: string
}

interface LogsResponse {
  logs: LogEntry[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

interface BackupPlan {
  id: string
  name: string
}

function formatFileSize(bytes: number | null): string {
  if (bytes === null || bytes === undefined) return "N/A"
  if (bytes === 0) return "0 B"
  
  const k = 1024
  const sizes = ["B", "KB", "MB", "GB", "TB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

function formatDateTime(dateTime: string): string {
  const date = new Date(dateTime)
  return date.toLocaleString('en-US', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZone: 'UTC'
  }) + ' UTC'
}

export function BackupLogs() {
  const navigate = useNavigate()
  const { planId } = useParams<{ planId: string }>()
  const [backupPlan, setBackupPlan] = useState<BackupPlan | null>(null)
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(100)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)

  useEffect(() => {
    const fetchData = async () => {
      if (!planId) {
        setError("Backup plan ID is required")
        setIsLoading(false)
        return
      }

      setIsLoading(true)
      setError(null)

      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        // Fetch backup plan name
        try {
          const planData: BackupPlan = await apiGet<BackupPlan>(`/api/backupplan/${planId}`)
          setBackupPlan(planData)
        } catch {
          // If plan fetch fails, continue without name
        }

        // Fetch logs
        const logsData: LogsResponse = await apiGet<LogsResponse>(
          `/api/backupplan/${planId}/logs?page=${page}&pageSize=${pageSize}`
        )
        setLogs(logsData.logs)
        setTotalPages(logsData.totalPages)
        setTotalCount(logsData.totalCount)
      } catch (err) {
        if (err instanceof TypeError && err.message === "Failed to fetch") {
          setError("Unable to connect to the server. Please make sure the backend is running.")
        } else {
          setError(err instanceof Error ? err.message : "An error occurred while fetching logs")
        }
      } finally {
        setIsLoading(false)
      }
    }

    fetchData()
  }, [planId, page, pageSize, navigate])

  const getActionColor = (action: string) => {
    switch (action) {
      case "Copy":
        return "bg-green-500/20 text-green-600 dark:text-green-400"
      case "Delete":
        return "bg-red-500/20 text-red-600 dark:text-red-400"
      case "Ignored":
        return "bg-gray-500/20 text-gray-600 dark:text-gray-400"
      default:
        return "bg-muted text-muted-foreground"
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/backup-plans")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Backup Plans
          </Button>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading logs...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/backup-plans")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Backup Plans
          </Button>
          <div>
            <h1 className="text-3xl font-bold">Backup Logs</h1>
            {backupPlan && (
              <p className="text-muted-foreground mt-2">
                {backupPlan.name}
              </p>
            )}
          </div>
        </div>
      </div>

      {error && (
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {!error && (
        <>
          <div className="rounded-lg border bg-card p-4 shadow-sm">
            <div className="flex items-center justify-between">
              <p className="text-sm text-muted-foreground">
                Total entries: <span className="font-medium text-foreground">{totalCount}</span>
              </p>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                  disabled={page === 1}
                >
                  <ChevronLeft className="h-4 w-4" />
                  Previous
                </Button>
                <span className="text-sm text-muted-foreground">
                  Page {page} of {totalPages}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                >
                  Next
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          </div>

          {logs.length === 0 ? (
            <div className="rounded-lg border bg-card p-6 shadow-sm">
              <div className="text-center py-12">
                <p className="text-muted-foreground">No logs found for this backup plan</p>
              </div>
            </div>
          ) : (
            <div className="rounded-lg border bg-card shadow-sm overflow-hidden">
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-muted">
                    <tr>
                      <th className="text-left p-3 text-sm font-medium">Date/Time</th>
                      <th className="text-left p-3 text-sm font-medium">File Name</th>
                      <th className="text-left p-3 text-sm font-medium">Size</th>
                      <th className="text-left p-3 text-sm font-medium">Action</th>
                      <th className="text-left p-3 text-sm font-medium">Reason</th>
                    </tr>
                  </thead>
                  <tbody>
                    {logs.map((log) => (
                      <tr key={log.id} className="border-t hover:bg-muted/50">
                        <td className="p-3 text-sm text-muted-foreground">
                          {formatDateTime(log.dateTime)}
                        </td>
                        <td className="p-3 text-sm">
                          <div className="max-w-md truncate" title={log.filePath}>
                            {log.fileName}
                          </div>
                        </td>
                        <td className="p-3 text-sm text-muted-foreground">
                          {formatFileSize(log.size)}
                        </td>
                        <td className="p-3 text-sm">
                          <span className={`px-2 py-1 rounded text-xs font-medium ${getActionColor(log.action)}`}>
                            {log.action}
                          </span>
                        </td>
                        <td className="p-3 text-sm text-muted-foreground">
                          {log.reason}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}

