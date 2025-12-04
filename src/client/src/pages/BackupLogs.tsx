import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ArrowLeft, ChevronLeft, ChevronRight, X, ArrowUpDown, ArrowUp, ArrowDown, Clock, CheckCircle2, Loader2, Copy, Check } from "lucide-react"
import { apiGet } from "@/lib/api"
import { formatDateTimeWithTimezone } from "@/components/TimezoneSelector"

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

interface BackupExecution {
  id: string
  name: string
  startDateTime: string
  endDateTime: string | null
}

interface ExecutionStats {
  executionId: string
  startDateTime: string
  endDateTime: string | null
  status: string
  currentFileName: string | null
  currentFilePath: string | null
  rsyncCommand: string
  // Rsync statistics
  totalFiles: number
  regularFiles: number
  directories: number
  createdFiles: number
  deletedFiles: number
  transferredFiles: number
  totalFileSize: number
  totalTransferredSize: number
  literalData: number
  matchedData: number
  fileListSize: number
  fileListGenerationTime: number
  fileListTransferTime: number
  totalBytesSent: number
  totalBytesReceived: number
  transferSpeedBytesPerSecond: number
  speedup: number
  durationSeconds: number
  // Progress tracking
  totalFilesToProcess: number | null
  currentFileIndex: number
}

function formatFileSize(bytes: number | null): string {
  if (bytes === null || bytes === undefined) return "N/A"
  if (bytes === 0) return "0 B"

  const k = 1024
  const sizes = ["B", "KB", "MB", "GB", "TB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}





function formatDateTime(dateTime: string, timezone: string = "UTC"): string {
  return formatDateTimeWithTimezone(dateTime, timezone)
}

function formatExecutionDateTime(dateTime: string, timezone: string = "UTC"): string {
  return formatDateTimeWithTimezone(dateTime, timezone)
}

function getStatusDisplay(status: string): { icon: React.ReactNode; color: string; text: string } {
  switch (status) {
    case "Starting":
      return {
        icon: <Loader2 className="h-5 w-5 animate-spin" />,
        color: "text-blue-600 dark:text-blue-400",
        text: "Starting..."
      }
    case "Analyzing":
      return {
        icon: <Loader2 className="h-5 w-5 animate-spin" />,
        color: "text-yellow-600 dark:text-yellow-400",
        text: "Analyzing Directory Structure"
      }
    case "Copying":
      return {
        icon: <Loader2 className="h-5 w-5 animate-spin" />,
        color: "text-orange-600 dark:text-orange-400",
        text: "Copying Files"
      }
    case "Finalizing":
      return {
        icon: <Loader2 className="h-5 w-5 animate-spin" />,
        color: "text-purple-600 dark:text-purple-400",
        text: "Finalizing Backup"
      }
    case "Finished":
      return {
        icon: <CheckCircle2 className="h-5 w-5" />,
        color: "text-green-600 dark:text-green-400",
        text: "Finished"
      }
    default:
      return {
        icon: <Clock className="h-5 w-5" />,
        color: "text-gray-600 dark:text-gray-400",
        text: "Unknown"
      }
  }
}

export function BackupLogs() {
  // Sleep for 1 second
  const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

  const navigate = useNavigate()
  const { planId, executionId } = useParams<{ planId: string; executionId?: string }>()
  const [backupPlan, setBackupPlan] = useState<BackupPlan | null>(null)
  const [executions, setExecutions] = useState<BackupExecution[]>([])
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [executionStats, setExecutionStats] = useState<ExecutionStats | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(100)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)

  // Filters
  const [filters, setFilters] = useState({
    action: "All",
    fileName: "",
    minSize: "",
    maxSize: "",
    fromDate: "",
    toDate: "",
  })

  // Debounced filename filter for search
  const [fileNameInput, setFileNameInput] = useState("")

  // Sorting
  const [sortBy, setSortBy] = useState("datetime")
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("desc")

  // Timezone from navbar selector
  const [timezone, setTimezone] = useState<string>("UTC")
  
  // Copy button state
  const [copied, setCopied] = useState(false)

  // Listen for timezone changes from navbar
  useEffect(() => {
    const handleTimezoneChange = (event: CustomEvent) => {
      const newTimezone = event.detail || "UTC"
      setTimezone(newTimezone)
    }

    window.addEventListener('timezoneChanged', handleTimezoneChange as EventListener)

    // Load initial timezone from sessionStorage
    const saved = sessionStorage.getItem("selectedTimezone")
    if (saved) {
      setTimezone(saved)
    }

    return () => {
      window.removeEventListener('timezoneChanged', handleTimezoneChange as EventListener)
    }
  }, [])

  // Fetch executions list
  useEffect(() => {
    const fetchExecutions = async () => {
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

        // Fetch executions
        const executionsData: BackupExecution[] = await apiGet<BackupExecution[]>(`/api/backupplan/${planId}/executions`)
        setExecutions(executionsData)
      } catch (err) {
        if (err instanceof TypeError && err.message === "Failed to fetch") {
          setError("Unable to connect to the server. Please make sure the backend is running.")
        } else {
          setError(err instanceof Error ? err.message : "An error occurred while fetching executions")
        }
      } finally {
        setIsLoading(false)
      }
    }

    // Only fetch executions if we're not viewing a specific execution
    if (!executionId) {
      fetchExecutions()
    }
  }, [planId, executionId, navigate])

  // Fetch logs for specific execution
  useEffect(() => {
    const fetchLogs = async () => {
      if (!planId || !executionId) {
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

        // Build query parameters
        const params = new URLSearchParams({
          executionId: executionId,
          page: page.toString(),
          pageSize: pageSize.toString(),
          sortBy: sortBy,
          sortOrder: sortOrder,
        })

        if (filters.action && filters.action !== "All") {
          params.append("action", filters.action)
        }
        if (filters.fileName) {
          params.append("fileName", filters.fileName)
        }
        if (filters.minSize) {
          params.append("minSize", filters.minSize)
        }
        if (filters.maxSize) {
          params.append("maxSize", filters.maxSize)
        }
        if (filters.fromDate) {
          params.append("fromDate", new Date(filters.fromDate).toISOString())
        }
        if (filters.toDate) {
          // Add time to end of day
          const toDate = new Date(filters.toDate)
          toDate.setHours(23, 59, 59, 999)
          params.append("toDate", toDate.toISOString())
        }

        // Fetch logs
        const logsData: LogsResponse = await apiGet<LogsResponse>(
          `/api/backupplan/${planId}/logs?${params.toString()}`
        )
        // Filter out internal system logs (rsync-stats, rsync-transfer-speed) from display
        const filteredLogs = logsData.logs.filter(
          (log) => log.fileName !== "rsync-stats" && log.fileName !== "rsync-transfer-speed"
        )
        setLogs(filteredLogs)
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

    fetchLogs()
  }, [planId, executionId, page, filters, sortBy, sortOrder, pageSize, navigate])

  // Fetch execution stats
  useEffect(() => {
    const fetchExecutionStats = async () => {
      if (!planId || !executionId) {
        return
      }

      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        const statsData: ExecutionStats = await apiGet<ExecutionStats>(
          `/api/backupplan/${planId}/executions/${executionId}/stats`
        )
        // Sleep for 1 second
        await sleep(1000);
        setExecutionStats(statsData)
      } catch (err) {
        console.error("Error fetching execution stats:", err)
        // Don't show error to user for stats, just log it
      }
    }

    fetchExecutionStats()

    // Auto-refresh stats if execution is not finished
    const interval = setInterval(() => {
      if (executionStats && executionStats.status !== "Finished") {
        fetchExecutionStats()
      }
    }, 3000) // Refresh every 3 seconds

    return () => clearInterval(interval)
  }, [planId, executionId, navigate, executionStats])

  // Debounce filename filter - wait 500ms after user stops typing
  useEffect(() => {
    const timer = setTimeout(() => {
      setFilters(prev => ({ ...prev, fileName: fileNameInput }))
      setPage(1)
    }, 500)

    return () => clearTimeout(timer)
  }, [fileNameInput])

  const handleFilterChange = (key: string, value: string) => {
    setFilters(prev => ({ ...prev, [key]: value }))
    setPage(1) // Reset to first page when filter changes
  }

  const clearFilters = () => {
    setFileNameInput("")
    setFilters({
      action: "All",
      fileName: "",
      minSize: "",
      maxSize: "",
      fromDate: "",
      toDate: "",
    })
    setPage(1)
  }

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc")
    } else {
      setSortBy(column)
      setSortOrder("asc")
    }
    setPage(1)
  }

  const getSortIcon = (column: string) => {
    if (sortBy !== column) {
      return <ArrowUpDown className="h-3 w-3 ml-1 inline" />
    }
    return sortOrder === "asc"
      ? <ArrowUp className="h-3 w-3 ml-1 inline" />
      : <ArrowDown className="h-3 w-3 ml-1 inline" />
  }

  const hasActiveFilters = filters.action !== "All" ||
    filters.fileName !== "" ||
    filters.minSize !== "" ||
    filters.maxSize !== "" ||
    filters.fromDate !== "" ||
    filters.toDate !== ""

  const getActionColor = (action: string) => {
    switch (action) {
      case "Copy":
        return "bg-green-500/20 text-green-600 dark:text-green-400"
      case "Delete":
        return "bg-red-500/20 text-red-600 dark:text-red-400"
      case "Ignored":
        return "bg-gray-500/20 text-gray-600 dark:text-gray-400"
      case "Milestone":
        return "bg-blue-500/20 text-blue-600 dark:text-blue-400"
      default:
        return "bg-muted text-muted-foreground"
    }
  }

  const handleExecutionClick = (execId: string) => {
    navigate(`/backup-plans/${planId}/logs/${execId}`)
  }

  const handleBackToExecutions = () => {
    navigate(`/backup-plans/${planId}/logs`)
  }

  const handleCopyCommand = async () => {
    if (executionStats?.rsyncCommand) {
      try {
        await navigator.clipboard.writeText(executionStats.rsyncCommand)
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
      } catch (err) {
        console.error("Failed to copy command:", err)
      }
    }
  }

  if (isLoading && !executionId) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/backup-plans")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Backup Plans
          </Button>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading executions...</p>
        </div>
      </div>
    )
  }

  // Show executions list if no executionId
  if (!executionId) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <Button variant="outline" onClick={() => navigate("/backup-plans")}>
              <ArrowLeft className="h-4 w-4 mr-2" />
              Back to Backup Plans
            </Button>
            <div>
              <h1 className="text-3xl font-bold">Backup Executions</h1>
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
            {executions.length === 0 ? (
              <div className="rounded-lg border bg-card p-6 shadow-sm">
                <div className="text-center py-12">
                  <p className="text-muted-foreground">No executions found for this backup plan</p>
                </div>
              </div>
            ) : (
              <div className="rounded-lg border bg-card shadow-sm overflow-hidden">
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <thead className="bg-muted">
                      <tr>
                        <th className="text-left p-3 text-sm font-medium">Execution</th>
                        <th className="text-left p-3 text-sm font-medium">Start Time</th>
                        <th className="text-left p-3 text-sm font-medium">End Time</th>
                        <th className="text-left p-3 text-sm font-medium">Status</th>
                        <th className="text-left p-3 text-sm font-medium">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {executions.map((execution) => (
                        <tr key={execution.id} className="border-t hover:bg-muted/50">
                          <td className="p-3 text-sm">
                            {execution.name}
                          </td>
                          <td className="p-3 text-sm text-muted-foreground">
                            {formatExecutionDateTime(execution.startDateTime, timezone)}
                          </td>
                          <td className="p-3 text-sm text-muted-foreground">
                            {execution.endDateTime
                              ? formatExecutionDateTime(execution.endDateTime, timezone)
                              : <span className="text-muted-foreground/50">In progress...</span>}
                          </td>
                          <td className="p-3 text-sm">
                            {execution.endDateTime ? (
                              <span className="flex items-center gap-1 text-green-600 dark:text-green-400">
                                <CheckCircle2 className="h-4 w-4" />
                                Completed
                              </span>
                            ) : (
                              <span className="flex items-center gap-1 text-blue-600 dark:text-blue-400">
                                <Clock className="h-4 w-4" />
                                Running
                              </span>
                            )}
                          </td>
                          <td className="p-3 text-sm">
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => handleExecutionClick(execution.id)}
                            >
                              View Logs
                            </Button>
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

  // Show logs for specific execution
  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={handleBackToExecutions}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Executions
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
          <Button variant="outline" onClick={handleBackToExecutions}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Executions
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
          {/* Execution Statistics */}
          {executionStats && (
            <div className="rounded-lg border bg-card p-6 shadow-sm">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold">Execution Statistics</h2>
                {(() => {
                  const statusDisplay = getStatusDisplay(executionStats.status)
                  return (
                    <div className={`flex items-center gap-2 ${statusDisplay.color} font-semibold`}>
                      {statusDisplay.icon}
                      <span>{statusDisplay.text}</span>
                    </div>
                  )
                })()}
              </div>
              
              {/* File Count Statistics Card */}
              <div className="rounded-lg border bg-muted/50 p-4 mb-4">
                <h3 className="text-sm font-semibold mb-4 text-muted-foreground">File Statistics</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                  <div className="space-y-1">
                    <p className="text-xs text-muted-foreground">Number of files</p>
                    <p className="text-xl font-bold">{executionStats.totalFiles.toLocaleString()} <small className="text-xs leading-none font-medium text-muted-foreground">(reg: {executionStats.regularFiles.toLocaleString()}, dir: {executionStats.directories.toLocaleString()})</small></p>
                  </div>

                  <div className="space-y-1">
                    <p className="text-xs text-muted-foreground">Number of created files</p>
                    <p className="text-xl font-bold">{executionStats.createdFiles.toLocaleString()}</p>
                  </div>

                  <div className="space-y-1">
                    <p className="text-xs text-muted-foreground">Number of deleted files</p>
                    <p className="text-xl font-bold">{executionStats.deletedFiles.toLocaleString()}</p>
                  </div>

                  <div className="space-y-1">
                    <p className="text-xs text-muted-foreground">Number of regular files transferred</p>
                    <p className="text-xl font-bold">{executionStats.transferredFiles.toLocaleString()}</p>
                  </div>
                </div>
              </div>

              {/* Other Statistics */}
              <div className="rounded-lg border bg-muted/50 p-4 mb-4">
                <h3 className="text-sm font-semibold mb-4 text-muted-foreground">File Statistics</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">

                 <div className="space-y-1">
                   <p className="text-xs text-muted-foreground">Total file size</p>
                   <p className="text-2xl font-bold">{formatFileSize(executionStats.totalFileSize)}</p>
                 </div>

                 <div className="space-y-1">
                   <p className="text-xs text-muted-foreground">Total transferred file size</p>
                   <p className="text-2xl font-bold">{formatFileSize(executionStats.totalTransferredSize)}</p>
                 </div>

                 <div className="space-y-1">
                   <p className="text-xs text-muted-foreground">File list size</p>
                   <p className="text-2xl font-bold">{formatFileSize(executionStats.fileListSize)}</p>
                 </div>

                <div className="space-y-1">
                  <p className="text-xs text-muted-foreground">File list generation time</p>
                  <p className="text-2xl font-bold">{executionStats.fileListGenerationTime.toFixed(3)} s</p>
                </div>

                <div className="space-y-1">
                  <p className="text-xs text-muted-foreground">File list transfer time</p>
                  <p className="text-2xl font-bold">{executionStats.fileListTransferTime.toFixed(3)} s</p>
                </div>

                 <div className="space-y-1">
                   <p className="text-xs text-muted-foreground">Total bytes sent</p>
                   <p className="text-2xl font-bold">{formatFileSize(executionStats.totalBytesSent)}</p>
                 </div>

                 <div className="space-y-1">
                   <p className="text-xs text-muted-foreground">Total bytes received</p>
                   <p className="text-2xl font-bold">{formatFileSize(executionStats.totalBytesReceived)}</p>
                 </div>

                </div>
              </div>


              
              {/* Rsync Command */}
              {executionStats.rsyncCommand && (
                <div className="mt-4 pt-4 border-t">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <p className="text-sm text-muted-foreground">Rsync Command</p>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={handleCopyCommand}
                        className="h-8"
                      >
                        {copied ? (
                          <>
                            <Check className="h-4 w-4 mr-2" />
                            Copied
                          </>
                        ) : (
                          <>
                            <Copy className="h-4 w-4 mr-2" />
                            Copy
                          </>
                        )}
                      </Button>
                    </div>
                    <div className="bg-muted rounded-md p-3">
                      <code className="text-sm break-all font-mono">
                        {executionStats.rsyncCommand}
                      </code>
                    </div>
                  </div>
                </div>
              )}



              {/* Current File Being Processed */}
              {executionStats.currentFileName && executionStats.status !== "Finished" && (
                <div className="mt-4 pt-4 border-t">
                  <div className="space-y-2">
                    <p className="text-sm text-muted-foreground">Currently Processing</p>
                    <div className="space-y-1">
                      <p className="text-lg font-semibold text-blue-600 dark:text-blue-400">
                        {executionStats.currentFileName}
                      </p>
                      {executionStats.currentFilePath && (
                        <p className="text-sm text-muted-foreground truncate" title={executionStats.currentFilePath}>
                          {executionStats.currentFilePath}
                        </p>
                      )}
                    </div>
                  </div>
                  
                  {/* Progress Bar */}
                  {executionStats.totalFilesToProcess !== null && executionStats.totalFilesToProcess > 0 && (
                    <div className="mt-4 space-y-2">
                      <div className="flex justify-between text-sm text-muted-foreground">
                        <span>Progress</span>
                        <span>
                          {executionStats.currentFileIndex} / {executionStats.totalFilesToProcess} files
                          {executionStats.totalFilesToProcess > 0 && (
                            <span className="ml-2">
                              ({Math.round((executionStats.currentFileIndex / executionStats.totalFilesToProcess) * 100)}%)
                            </span>
                          )}
                        </span>
                      </div>
                      <div className="w-full bg-secondary rounded-full h-2.5 overflow-hidden">
                        <div
                          className="bg-primary h-2.5 rounded-full transition-all duration-300 ease-out"
                          style={{
                            width: executionStats.totalFilesToProcess > 0
                              ? `${Math.min((executionStats.currentFileIndex / executionStats.totalFilesToProcess) * 100, 100)}%`
                              : '0%'
                          }}
                        />
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* Filters Section */}
          <div className="rounded-lg border bg-card p-4 shadow-sm">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h2 className="text-lg font-semibold">Filters</h2>
                {hasActiveFilters && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={clearFilters}
                  >
                    <X className="h-4 w-4 mr-2" />
                    Clear Filters
                  </Button>
                )}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {/* Action Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-action">Action</Label>
                  <select
                    id="filter-action"
                    value={filters.action}
                    onChange={(e) => handleFilterChange("action", e.target.value)}
                    className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                  >
                    <option value="All">All</option>
                    <option value="Copy">Copy</option>
                    <option value="Delete">Delete</option>
                    <option value="Ignored">Ignored</option>
                    <option value="Milestone">Milestone</option>
                  </select>
                </div>

                {/* Filename Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-filename">File Name</Label>
                  <Input
                    id="filter-filename"
                    type="text"
                    placeholder="Search filename..."
                    value={fileNameInput}
                    onChange={(e) => setFileNameInput(e.target.value)}
                  />
                </div>

                {/* Min Size Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-minsize">Min Size (bytes)</Label>
                  <Input
                    id="filter-minsize"
                    type="number"
                    placeholder="0"
                    value={filters.minSize}
                    onChange={(e) => handleFilterChange("minSize", e.target.value)}
                    min="0"
                  />
                </div>

                {/* Max Size Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-maxsize">Max Size (bytes)</Label>
                  <Input
                    id="filter-maxsize"
                    type="number"
                    placeholder="No limit"
                    value={filters.maxSize}
                    onChange={(e) => handleFilterChange("maxSize", e.target.value)}
                    min="0"
                  />
                </div>

                {/* From Date Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-fromdate">From Date</Label>
                  <Input
                    id="filter-fromdate"
                    type="date"
                    value={filters.fromDate}
                    onChange={(e) => handleFilterChange("fromDate", e.target.value)}
                  />
                </div>

                {/* To Date Filter */}
                <div className="space-y-2">
                  <Label htmlFor="filter-todate">To Date</Label>
                  <Input
                    id="filter-todate"
                    type="date"
                    value={filters.toDate}
                    onChange={(e) => handleFilterChange("toDate", e.target.value)}
                  />
                </div>
              </div>
            </div>
          </div>

          {/* Sort Section */}
          <div className="rounded-lg border bg-card p-4 shadow-sm">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-4">
                <Label htmlFor="sort-by">Sort By:</Label>
                <select
                  id="sort-by"
                  value={sortBy}
                  onChange={(e) => {
                    setSortBy(e.target.value)
                    setPage(1)
                  }}
                  className="rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                >
                  <option value="datetime">Date/Time</option>
                  <option value="filename">File Name</option>
                  <option value="size">Size</option>
                  <option value="action">Action</option>
                </select>

                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setSortOrder(sortOrder === "asc" ? "desc" : "asc")
                    setPage(1)
                  }}
                >
                  {sortOrder === "asc" ? (
                    <>
                      <ArrowUp className="h-4 w-4 mr-2" />
                      Ascending
                    </>
                  ) : (
                    <>
                      <ArrowDown className="h-4 w-4 mr-2" />
                      Descending
                    </>
                  )}
                </Button>
              </div>

              <div className="flex items-center gap-2">
                <p className="text-sm text-muted-foreground">
                  Total entries: <span className="font-medium text-foreground">{totalCount}</span>
                </p>
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
                <p className="text-muted-foreground">No logs found for this execution</p>
              </div>
            </div>
          ) : (
            <div className="rounded-lg border bg-card shadow-sm overflow-hidden">
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-muted">
                    <tr>
                      <th
                        className="text-left p-3 text-sm font-medium cursor-pointer hover:bg-muted/80 select-none"
                        onClick={() => handleSort("datetime")}
                      >
                        Date/Time{getSortIcon("datetime")}
                      </th>
                      <th
                        className="text-left p-3 text-sm font-medium cursor-pointer hover:bg-muted/80 select-none"
                        onClick={() => handleSort("filename")}
                      >
                        File Name{getSortIcon("filename")}
                      </th>
                      <th
                        className="text-left p-3 text-sm font-medium cursor-pointer hover:bg-muted/80 select-none"
                        onClick={() => handleSort("size")}
                      >
                        Size{getSortIcon("size")}
                      </th>
                      <th
                        className="text-left p-3 text-sm font-medium cursor-pointer hover:bg-muted/80 select-none"
                        onClick={() => handleSort("action")}
                      >
                        Action{getSortIcon("action")}
                      </th>
                      <th className="text-left p-3 text-sm font-medium">Reason</th>
                    </tr>
                  </thead>
                  <tbody>
                    {logs.map((log) => (
                      <tr key={log.id} className="border-t hover:bg-muted/50">
                        <td className="p-3 text-sm text-muted-foreground">
                          {formatDateTime(log.dateTime, timezone)}
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
