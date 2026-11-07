import { useEffect, useState, useCallback, useRef } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { apiGet, apiPost } from "@/lib/api"
import {
  ArrowLeft,
  Loader2,
  CheckCircle2,
  XCircle,
  Clock,
  Terminal,
  Copy,
  Check,
  RefreshCw,
  Square,
  ArrowDownToLine,
} from "lucide-react"

interface Execution {
  id: string
  extensionId: string
  extensionName: string
  command: string
  startDateTime: string
  endDateTime?: string
  exitCode?: number
  output?: string
  status: string
  isAutomatic: boolean
}

export function ExtensionLogs() {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()

  const [executions, setExecutions] = useState<Execution[]>([])
  const [selectedExecutionId, setSelectedExecutionId] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Default auto-refresh interval: 5 seconds
  const [refreshInterval, setRefreshInterval] = useState<number>(5)
  const [isStopping, setIsStopping] = useState(false)
  const [autoScroll, setAutoScroll] = useState(true)

  const terminalBottomRef = useRef<HTMLDivElement>(null)

  const selectedExecution = executions.find((e) => e.id === selectedExecutionId) || executions[0] || null

  // Auto-scroll terminal when output updates and autoScroll is active
  useEffect(() => {
    if (autoScroll && selectedExecution?.status === "Running" && terminalBottomRef.current) {
      terminalBottomRef.current.scrollIntoView({ behavior: "smooth" })
    }
  }, [selectedExecution?.output, autoScroll, selectedExecution?.status])

  const fetchExecutions = useCallback(async (isManualRefresh = false) => {
    if (isManualRefresh) setIsRefreshing(true)
    setError(null)

    try {
      const data = await apiGet<Execution[]>(`/api/extension/${id}/executions`)
      setExecutions(data)
      setSelectedExecutionId((prev) => {
        if (!prev && data.length > 0) return data[0].id
        if (prev && data.some((e) => e.id === prev)) return prev
        return data.length > 0 ? data[0].id : null
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load execution logs")
    } finally {
      setIsLoading(false)
      setIsRefreshing(false)
    }
  }, [id])

  useEffect(() => {
    if (!id) return

    fetchExecutions()

    if (refreshInterval > 0) {
      const timer = setInterval(() => {
        fetchExecutions(false)
      }, refreshInterval * 1000)

      return () => clearInterval(timer)
    }
  }, [id, fetchExecutions, refreshInterval])

  const handleCopyOutput = () => {
    if (selectedExecution?.output) {
      navigator.clipboard.writeText(selectedExecution.output)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const handleStopExecution = async () => {
    if (!id || !selectedExecution) return
    setIsStopping(true)
    setError(null)
    try {
      await apiPost(`/api/extension/${id}/executions/${selectedExecution.id}/stop`)
      await fetchExecutions(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to stop extension process")
    } finally {
      setIsStopping(false)
    }
  }

  const formatDuration = (start: string, end?: string) => {
    if (!end) return "Running..."
    const ms = new Date(end).getTime() - new Date(start).getTime()
    if (ms < 1000) return `${ms}ms`
    const sec = (ms / 1000).toFixed(1)
    return `${sec}s`
  }

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center gap-4">
          <Button variant="outline" size="icon" onClick={() => navigate("/extensions")}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-3xl font-bold tracking-tight">Extension Execution Logs</h1>
            <p className="text-muted-foreground">
              {executions[0]?.extensionName
                ? `History and console output for '${executions[0].extensionName}'`
                : "View execution history and detailed console output"}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground bg-card border rounded-md px-2.5 py-1.5 shadow-sm">
            <Clock className="h-3.5 w-3.5 text-muted-foreground" />
            <span className="font-medium text-foreground">Auto-refresh:</span>
            <select
              value={refreshInterval}
              onChange={(e) => setRefreshInterval(Number(e.target.value))}
              aria-label="Intervalo de atualização automática"
              className="bg-transparent text-xs font-medium text-foreground focus:outline-none cursor-pointer"
            >
              <option value={0} className="bg-popover text-popover-foreground">Off</option>
              <option value={2} className="bg-popover text-popover-foreground">2s</option>
              <option value={5} className="bg-popover text-popover-foreground">5s (Default)</option>
              <option value={10} className="bg-popover text-popover-foreground">10s</option>
              <option value={30} className="bg-popover text-popover-foreground">30s</option>
              <option value={60} className="bg-popover text-popover-foreground">60s</option>
            </select>
          </div>

          <Button
            variant="outline"
            size="sm"
            onClick={() => fetchExecutions(true)}
            disabled={isRefreshing}
          >
            <RefreshCw className={`h-4 w-4 mr-2 ${isRefreshing ? "animate-spin" : ""}`} />
            Refresh
          </Button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg bg-destructive/15 border border-destructive/20 p-4 text-destructive">
          <p className="font-medium">{error}</p>
        </div>
      )}

      {executions.length === 0 ? (
        <div className="rounded-lg border border-dashed p-12 text-center">
          <Clock className="mx-auto h-8 w-8 text-muted-foreground mb-3" />
          <h3 className="text-lg font-semibold">No executions recorded yet</h3>
          <p className="text-sm text-muted-foreground mt-1 mb-4">
            Trigger a manual run from the extensions page or wait for the scheduled cron run.
          </p>
          <Button variant="outline" onClick={() => navigate("/extensions")}>
            Back to Extensions
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Execution list sidebar */}
          <div className="space-y-3">
            <h3 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
              Executions ({executions.length})
            </h3>
            <div className="space-y-2 max-h-[650px] overflow-y-auto pr-1">
              {executions.map((exec) => {
                const isSelected = selectedExecution?.id === exec.id

                return (
                  <div
                    key={exec.id}
                    onClick={() => setSelectedExecutionId(exec.id)}
                    className={`p-3 rounded-lg border cursor-pointer transition-all ${
                      isSelected
                        ? "border-primary bg-primary/5 shadow-sm"
                        : "bg-card hover:border-muted-foreground/30"
                    }`}
                  >
                    <div className="flex items-center justify-between mb-1.5">
                      <span className="text-xs font-medium text-muted-foreground">
                        {new Date(exec.startDateTime).toLocaleString()}
                      </span>
                      {exec.status === "Success" && (
                        <span className="inline-flex items-center gap-1 text-xs text-green-600 dark:text-green-400 font-medium">
                          <CheckCircle2 className="h-3.5 w-3.5" />
                          Success
                        </span>
                      )}
                      {exec.status === "Failed" && (
                        <span className="inline-flex items-center gap-1 text-xs text-red-600 dark:text-red-400 font-medium">
                          <XCircle className="h-3.5 w-3.5" />
                          Failed ({exec.exitCode ?? "?"})
                        </span>
                      )}
                      {exec.status === "Stopped" && (
                        <span className="inline-flex items-center gap-1 text-xs text-amber-600 dark:text-amber-400 font-medium">
                          <Square className="h-3.5 w-3.5" />
                          Stopped
                        </span>
                      )}
                      {exec.status === "Running" && (
                        <span className="inline-flex items-center gap-1 text-xs text-blue-600 dark:text-blue-400 font-medium">
                          <Loader2 className="h-3.5 w-3.5 animate-spin" />
                          Running
                        </span>
                      )}
                    </div>

                    <div className="flex items-center justify-between text-xs">
                      <span className="font-mono text-muted-foreground truncate max-w-[170px]">
                        {exec.command}
                      </span>
                      <span className="text-muted-foreground">
                        {formatDuration(exec.startDateTime, exec.endDateTime)}
                      </span>
                    </div>

                    <div className="mt-1 flex items-center gap-2">
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-muted text-muted-foreground uppercase">
                        {exec.isAutomatic ? "Cron" : "Manual"}
                      </span>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          {/* Console output display */}
          <div className="lg:col-span-2 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <Terminal className="h-4 w-4 text-muted-foreground" />
                <h3 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
                  Console Output
                </h3>
                {selectedExecution?.status === "Running" && (
                  <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium bg-blue-500/10 text-blue-500 border border-blue-500/20 animate-pulse">
                    <span className="h-2 w-2 rounded-full bg-blue-500" />
                    Live Output
                  </span>
                )}
              </div>

              <div className="flex items-center gap-2">
                {selectedExecution?.status === "Running" && (
                  <>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setAutoScroll((prev) => !prev)}
                      className={`text-xs h-8 ${autoScroll ? "bg-muted text-foreground" : "text-muted-foreground"}`}
                      title={autoScroll ? "Auto-scroll enabled" : "Auto-scroll disabled"}
                    >
                      <ArrowDownToLine className={`h-3.5 w-3.5 mr-1 ${autoScroll ? "text-primary" : "text-muted-foreground"}`} />
                      Auto-scroll: {autoScroll ? "On" : "Off"}
                    </Button>

                    <Button
                      variant="destructive"
                      size="sm"
                      className="h-8 text-xs font-medium"
                      onClick={handleStopExecution}
                      disabled={isStopping}
                      title="Stop running process"
                    >
                      {isStopping ? (
                        <>
                          <Loader2 className="h-3.5 w-3.5 mr-1.5 animate-spin" />
                          Stopping...
                        </>
                      ) : (
                        <>
                          <Square className="h-3.5 w-3.5 mr-1.5 fill-current" />
                          Stop Process
                        </>
                      )}
                    </Button>
                  </>
                )}

                {selectedExecution?.output && (
                  <Button variant="ghost" size="sm" onClick={handleCopyOutput}>
                    {copied ? (
                      <>
                        <Check className="h-3.5 w-3.5 mr-1 text-green-500" />
                        Copied!
                      </>
                    ) : (
                      <>
                        <Copy className="h-3.5 w-3.5 mr-1" />
                        Copy Output
                      </>
                    )}
                  </Button>
                )}
              </div>
            </div>

            {selectedExecution ? (
              <div className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-lg border bg-muted/40 text-xs">
                  <div className="flex flex-wrap items-center gap-3">
                    <div>
                      <span className="text-muted-foreground">Command: </span>
                      <code className="font-mono font-semibold">{selectedExecution.command}</code>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Started: </span>
                      <span>{new Date(selectedExecution.startDateTime).toLocaleTimeString()}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Duration: </span>
                      <span>{formatDuration(selectedExecution.startDateTime, selectedExecution.endDateTime)}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Exit Code: </span>
                      <span className="font-semibold">
                        {selectedExecution.exitCode ?? (selectedExecution.status === "Running" ? "Running..." : "N/A")}
                      </span>
                    </div>
                  </div>
                  <div>
                    {selectedExecution.status === "Running" && (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-xs font-medium bg-blue-500/10 text-blue-500">
                        <Loader2 className="h-3 w-3 animate-spin" /> Running
                      </span>
                    )}
                    {selectedExecution.status === "Success" && (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-xs font-medium bg-green-500/10 text-green-600 dark:text-green-400">
                        <CheckCircle2 className="h-3 w-3" /> Success
                      </span>
                    )}
                    {selectedExecution.status === "Failed" && (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-xs font-medium bg-red-500/10 text-red-600 dark:text-red-400">
                        <XCircle className="h-3 w-3" /> Failed
                      </span>
                    )}
                    {selectedExecution.status === "Stopped" && (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-xs font-medium bg-amber-500/10 text-amber-600 dark:text-amber-400">
                        <Square className="h-3 w-3" /> Stopped
                      </span>
                    )}
                  </div>
                </div>

                <div className="relative rounded-lg bg-zinc-950 p-4 border border-zinc-800 text-zinc-100 font-mono text-xs overflow-x-auto min-h-[450px] max-h-[600px] shadow-inner">
                  {selectedExecution.output ? (
                    <>
                      <pre className="whitespace-pre-wrap break-all leading-relaxed">
                        {selectedExecution.output}
                      </pre>
                      {selectedExecution.status === "Running" && (
                        <div className="flex items-center gap-2 pt-3 text-zinc-400 border-t border-zinc-800/80 mt-3">
                          <Loader2 className="h-3.5 w-3.5 animate-spin text-blue-400" />
                          <span className="text-[11px]">
                            Process is running... output updating live (Refresh: {refreshInterval > 0 ? `${refreshInterval}s` : "Off"})
                          </span>
                        </div>
                      )}
                      <div ref={terminalBottomRef} />
                    </>
                  ) : selectedExecution.status === "Running" ? (
                    <div className="flex items-center gap-2 text-zinc-400">
                      <Loader2 className="h-4 w-4 animate-spin text-primary" />
                      <span>Process is currently executing... Waiting for initial output</span>
                    </div>
                  ) : (
                    <p className="text-zinc-500 italic">No output captured for this execution.</p>
                  )}
                </div>
              </div>
            ) : (
              <div className="flex items-center justify-center h-64 border rounded-lg text-muted-foreground">
                Select an execution to view its output
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
