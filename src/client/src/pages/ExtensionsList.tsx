import { useEffect, useState, useCallback } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Switch } from "@/components/ui/switch"
import { Plus, Pencil, Trash2, FileText, Play, Loader2, Blocks, CheckCircle2, XCircle, Clock, KeyRound, Square } from "lucide-react"
import { apiGet, apiPost, apiDelete } from "@/lib/api"
import { CronDescription } from "@/components/CronDescription"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"

export interface ExtensionItem {
  id: string
  name: string
  description?: string
  command: string
  schedule: string
  active: boolean
  folderPath: string
  environmentVariables?: string
  createdAt: string
  updatedAt: string
  lastRunAt?: string
  lastExitCode?: number
  lastStatus?: string
  isRunning?: boolean
  nextRunAt?: string
}

export function ExtensionsList() {
  const navigate = useNavigate()
  const [extensions, setExtensions] = useState<ExtensionItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [runningExtensions, setRunningExtensions] = useState<Record<string, boolean>>({})
  const [stoppingExtensions, setStoppingExtensions] = useState<Record<string, boolean>>({})
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  const fetchExtensions = useCallback(async () => {
    try {
      const data = await apiGet<ExtensionItem[]>("/api/extension")
      setExtensions(data)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "Failed to load extensions")
      }
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    fetchExtensions()
  }, [fetchExtensions])

  // Periodic poll if any extension is running
  useEffect(() => {
    const hasRunning = extensions.some((e) => e.isRunning || e.lastStatus === "Running") ||
      Object.values(runningExtensions).some(Boolean)

    if (hasRunning) {
      const interval = setInterval(() => {
        fetchExtensions()
      }, 5000)
      return () => clearInterval(interval)
    }
  }, [extensions, runningExtensions, fetchExtensions])

  const handleToggleActive = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation()
    try {
      await apiPost(`/api/extension/${id}/toggle-active`)
      await fetchExtensions()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to toggle extension status")
    }
  }

  const handleStopExtension = async (id: string, name: string, e: React.MouseEvent) => {
    e.stopPropagation()
    setStoppingExtensions((prev) => ({ ...prev, [id]: true }))
    setError(null)

    try {
      await apiPost(`/api/extension/${id}/stop`)
      setSuccessMessage(`Termination signal sent to extension '${name}'.`)
      await fetchExtensions()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to stop extension")
    } finally {
      setStoppingExtensions((prev) => ({ ...prev, [id]: false }))
    }
  }

  const handleExecuteNow = async (id: string, name: string, e: React.MouseEvent) => {
    e.stopPropagation()
    setRunningExtensions((prev) => ({ ...prev, [id]: true }))
    setSuccessMessage(null)
    setError(null)

    try {
      const result = await apiPost(`/api/extension/${id}/execute`)
      if (result.status === "Success") {
        setSuccessMessage(`Extension '${name}' executed successfully (Exit Code: 0).`)
      } else {
        setError(`Extension '${name}' finished with status: ${result.status} (Exit Code: ${result.exitCode}). Check logs for details.`)
      }
      await fetchExtensions()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to execute extension")
    } finally {
      setRunningExtensions((prev) => ({ ...prev, [id]: false }))
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await apiDelete(`/api/extension/${id}`)
      await fetchExtensions()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to delete extension")
    }
  }

  const getStatusBadge = (status?: string, exitCode?: number) => {
    switch (status) {
      case "Success":
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full bg-green-500/15 text-green-700 dark:text-green-400">
            <CheckCircle2 className="h-3.5 w-3.5" />
            Success (0)
          </span>
        )
      case "Failed":
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full bg-red-500/15 text-red-700 dark:text-red-400">
            <XCircle className="h-3.5 w-3.5" />
            Failed {exitCode !== undefined ? `(${exitCode})` : ""}
          </span>
        )
      case "Stopped":
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full bg-amber-500/15 text-amber-700 dark:text-amber-400">
            <Square className="h-3.5 w-3.5" />
            Stopped
          </span>
        )
      case "Running":
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full bg-blue-500/15 text-blue-700 dark:text-blue-400">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Running
          </span>
        )
      default:
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full bg-muted text-muted-foreground">
            <Clock className="h-3.5 w-3.5" />
            Never Run
          </span>
        )
    }
  }

  const getEnvVarCount = (envJson?: string) => {
    if (!envJson) return 0
    try {
      const parsed = JSON.parse(envJson)
      if (Array.isArray(parsed)) return parsed.length
      if (typeof parsed === "object" && parsed !== null) return Object.keys(parsed).length
    } catch {
      // Ignore
    }
    return 0
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
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Extensions</h1>
          <p className="text-muted-foreground">
            Upload, configure, and orchestrate external .NET applications with cron scheduling.
          </p>
        </div>
        <Button onClick={() => navigate("/extensions/add")}>
          <Plus className="mr-2 h-4 w-4" />
          Add Extension
        </Button>
      </div>

      {error && (
        <div className="rounded-lg bg-destructive/15 border border-destructive/20 p-4 text-destructive">
          <p className="font-medium">{error}</p>
        </div>
      )}

      {successMessage && (
        <div className="rounded-lg bg-green-500/15 border border-green-500/20 p-4 text-green-700 dark:text-green-400">
          <p className="font-medium">{successMessage}</p>
        </div>
      )}

      {extensions.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed p-12 text-center">
          <div className="rounded-full bg-muted p-4 mb-4">
            <Blocks className="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 className="text-lg font-semibold">No extensions installed</h3>
          <p className="text-sm text-muted-foreground mt-1 mb-4 max-w-sm">
            Upload your first .NET application as a .zip package to run automated tasks alongside your backups.
          </p>
          <Button onClick={() => navigate("/extensions/add")}>
            <Plus className="mr-2 h-4 w-4" />
            Add Extension
          </Button>
        </div>
      ) : (
        <div className="grid gap-4">
          {extensions.map((ext) => {
            const isExecuting = runningExtensions[ext.id] || ext.isRunning || ext.lastStatus === "Running"
            const isStopping = stoppingExtensions[ext.id]

            return (
              <div
                key={ext.id}
                className="rounded-lg border bg-card p-6 shadow-sm hover:shadow-md transition-shadow"
              >
                <div className="flex flex-col md:flex-row md:items-start justify-between gap-4">
                  <div className="space-y-3 flex-1">
                    <div className="flex flex-wrap items-center gap-3">
                      <h3 className="text-xl font-semibold">{ext.name}</h3>
                      {getStatusBadge(ext.lastStatus, ext.lastExitCode ?? undefined)}
                      {getEnvVarCount(ext.environmentVariables) > 0 && (
                        <span className="inline-flex items-center gap-1 text-xs font-medium bg-muted text-muted-foreground px-2 py-0.5 rounded-full">
                          <KeyRound className="h-3 w-3" />
                          {getEnvVarCount(ext.environmentVariables)} Env Var{getEnvVarCount(ext.environmentVariables) > 1 ? "s" : ""}
                        </span>
                      )}
                      <div className="flex items-center gap-2 ml-auto md:ml-0">
                        <Switch
                          checked={ext.active}
                          onCheckedChange={() => {}}
                          onClick={(e) => handleToggleActive(ext.id, e)}
                        />
                        <span className="text-xs text-muted-foreground font-medium">
                          {ext.active ? "Active" : "Inactive"}
                        </span>
                      </div>
                    </div>

                    {ext.description && (
                      <p className="text-sm text-muted-foreground">{ext.description}</p>
                    )}

                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 pt-2">
                      <div>
                        <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-1">
                          Command
                        </p>
                        <code className="text-xs font-mono bg-muted px-2 py-1 rounded inline-block">
                          {ext.command}
                        </code>
                      </div>

                      <div>
                        <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-1">
                          Schedule
                        </p>
                        <p className="text-xs font-mono mb-1">{ext.schedule}</p>
                        <CronDescription cronExpression={ext.schedule} />
                      </div>

                      <div>
                        <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-1">
                          Executions
                        </p>
                        <p className="text-xs text-muted-foreground">
                          Last: {ext.lastRunAt ? new Date(ext.lastRunAt).toLocaleString() : "Never"}
                        </p>
                        {ext.nextRunAt && ext.active && (
                          <p className="text-xs text-muted-foreground">
                            Next: {new Date(ext.nextRunAt).toLocaleString()}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 self-end md:self-start">
                    {isExecuting ? (
                      <Button
                        variant="destructive"
                        size="sm"
                        disabled={isStopping}
                        onClick={(e) => handleStopExtension(ext.id, ext.name, e)}
                        title="Stop running extension"
                      >
                        {isStopping ? (
                          <>
                            <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />
                            Stopping...
                          </>
                        ) : (
                          <>
                            <Square className="h-4 w-4 mr-1.5 fill-current" />
                            Stop
                          </>
                        )}
                      </Button>
                    ) : (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={(e) => handleExecuteNow(ext.id, ext.name, e)}
                        title="Run extension now"
                      >
                        <Play className="h-4 w-4 mr-1.5 text-primary" />
                        Run Now
                      </Button>
                    )}

                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => navigate(`/extensions/${ext.id}/logs`)}
                      title="View execution logs"
                    >
                      <FileText className="h-4 w-4 mr-1.5" />
                      Logs
                    </Button>

                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => navigate(`/extensions/${ext.id}/edit`)}
                      title="Edit extension"
                    >
                      <Pencil className="h-4 w-4 mr-1.5" />
                      Edit
                    </Button>

                    <AlertDialog>
                      <AlertDialogTrigger asChild>
                        <Button
                          variant="outline"
                          size="sm"
                          className="text-destructive hover:text-destructive"
                          title="Delete extension"
                        >
                          <Trash2 className="h-4 w-4 mr-1.5" />
                          Delete
                        </Button>
                      </AlertDialogTrigger>
                      <AlertDialogContent>
                        <AlertDialogHeader>
                          <AlertDialogTitle>Are you sure?</AlertDialogTitle>
                          <AlertDialogDescription>
                            This will permanently delete the extension <strong>{ext.name}</strong>,
                            remove all unpacked files from disk, and delete its execution logs.
                          </AlertDialogDescription>
                        </AlertDialogHeader>
                        <AlertDialogFooter>
                          <AlertDialogCancel>Cancel</AlertDialogCancel>
                          <AlertDialogAction
                            onClick={() => handleDelete(ext.id)}
                            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                          >
                            Delete Extension
                          </AlertDialogAction>
                        </AlertDialogFooter>
                      </AlertDialogContent>
                    </AlertDialog>
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

