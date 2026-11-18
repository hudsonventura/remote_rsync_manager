import { useState } from "react"
import { 
  Archive, 
  Download, 
  Upload, 
  CheckCircle2, 
  AlertTriangle, 
  Server, 
  Users, 
  Database, 
  FileJson, 
  ArrowRight, 
  RefreshCw,
  Info
} from "lucide-react"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { apiPost, API_URL } from "@/lib/api"

interface ExportedUser {
  id: string
  username: string
  email: string
  isAdmin: boolean
  timezone?: string
  theme?: string
  createdAt: string
  isActive: boolean
}

interface ExportedAgent {
  id: string
  name: string
  hostname: string
  rsyncUser?: string
  rsyncPort: number
  rsyncSshKey?: string
}

interface ExportedBackupPlan {
  id: string
  name: string
  description?: string
  schedule: string
  source: string
  destination: string
  active: boolean
  agentId?: string
  agentName?: string
}

interface ConfigExportPayload {
  metadata: {
    version: string
    exportedAt: string
    serverVersion: string
  }
  users: ExportedUser[]
  agents: ExportedAgent[]
  backupPlans: ExportedBackupPlan[]
}

interface UserConflict {
  imported: ExportedUser
  existing: ExportedUser
  reason: string
}

interface AgentConflict {
  imported: ExportedAgent
  existing: ExportedAgent
  reason: string
  associatedPlans: ExportedBackupPlan[]
}

interface BackupPlanConflict {
  imported: ExportedBackupPlan
  existing: ExportedBackupPlan
  reason: string
}

interface SimpleAgentSummary {
  id: string
  name: string
  hostname: string
}

interface ConflictPreviewResponse {
  newUsers: ExportedUser[]
  conflictedUsers: UserConflict[]
  newAgents: ExportedAgent[]
  conflictedAgents: AgentConflict[]
  newBackupPlans: ExportedBackupPlan[]
  conflictedBackupPlans: BackupPlanConflict[]
  existingAgents: SimpleAgentSummary[]
}

interface ImportResult {
  success: boolean
  message: string
  usersImported: number
  usersUpdated: number
  usersSkipped: number
  agentsImported: number
  agentsUpdated: number
  agentsSkipped: number
  plansImported: number
  plansUpdated: number
  plansSkipped: number
}

export function BackupRestore() {
  const [isExporting, setIsExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [parsedPayload, setParsedPayload] = useState<ConfigExportPayload | null>(null)
  const [isPreviewing, setIsPreviewing] = useState(false)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [previewData, setPreviewData] = useState<ConflictPreviewResponse | null>(null)

  // Resolution state maps: id -> action
  const [userResolutions, setUserResolutions] = useState<Record<string, "keep_existing" | "overwrite" | "skip">>({})
  const [agentResolutions, setAgentResolutions] = useState<Record<string, "keep_existing" | "overwrite" | "create_new" | "skip">>({})
  const [agentPlanMappings, setAgentPlanMappings] = useState<Record<string, string>>({})
  const [planResolutions, setPlanResolutions] = useState<Record<string, "keep_existing" | "overwrite" | "create_new" | "skip">>({})

  const [isImporting, setIsImporting] = useState(false)
  const [importResult, setImportResult] = useState<ImportResult | null>(null)

  // 1. Export handler
  const handleExport = async () => {
    setIsExporting(true)
    setExportError(null)

    try {
      const url = API_URL ? `${API_URL}/api/backup-restore/export` : "/api/backup-restore/export"
      const token = sessionStorage.getItem("token")
      const response = await fetch(url, {
        method: "GET",
        headers: token ? { Authorization: `Bearer ${token}` } : {}
      })

      if (!response.ok) {
        throw new Error(`Export failed with status ${response.status}`)
      }

      const blob = await response.blob()

      // Extract filename from header or fallback
      let fileName = `RemoteRSyncManager-${new Date().toISOString().replace(/[:.]/g, "-")}.json`
      const disposition = response.headers.get("content-disposition")
      if (disposition && disposition.indexOf("filename=") !== -1) {
        const matches = /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/.exec(disposition)
        if (matches != null && matches[1]) {
          fileName = matches[1].replace(/['"]/g, "")
        }
      }

      const downloadUrl = window.URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = downloadUrl
      a.download = fileName
      document.body.appendChild(a)
      a.click()
      window.URL.revokeObjectURL(downloadUrl)
      document.body.removeChild(a)
    } catch (err) {
      console.error("Export error:", err)
      setExportError(err instanceof Error ? err.message : "Failed to export configuration")
    } finally {
      setIsExporting(false)
    }
  }

  // 2. File Selection & Preview
  const handleFileChange = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return

    setSelectedFile(file)
    setPreviewError(null)
    setImportResult(null)
    setIsPreviewing(true)

    try {
      const fileText = await file.text()
      const payload: ConfigExportPayload = JSON.parse(fileText)

      if (!payload.users || !payload.agents || !payload.backupPlans) {
        throw new Error("Invalid configuration file format. Missing users, agents, or backup plans.")
      }

      setParsedPayload(payload)

      // Request conflict preview from backend
      const preview = await apiPost<ConflictPreviewResponse>("/api/backup-restore/preview", payload)
      setPreviewData(preview)

      // Initialize default resolution actions
      const initialUserRes: Record<string, "keep_existing" | "overwrite" | "skip"> = {}
      preview.conflictedUsers.forEach(u => {
        initialUserRes[u.imported.id] = "keep_existing"
      })
      setUserResolutions(initialUserRes)

      const initialAgentRes: Record<string, "keep_existing" | "overwrite" | "create_new" | "skip"> = {}
      const initialAgentMappings: Record<string, string> = {}
      preview.conflictedAgents.forEach(a => {
        initialAgentRes[a.imported.id] = "keep_existing"
        initialAgentMappings[a.imported.id] = a.existing.id
      })
      setAgentResolutions(initialAgentRes)
      setAgentPlanMappings(initialAgentMappings)

      const initialPlanRes: Record<string, "keep_existing" | "overwrite" | "create_new" | "skip"> = {}
      preview.conflictedBackupPlans.forEach(p => {
        initialPlanRes[p.imported.id] = "keep_existing"
      })
      setPlanResolutions(initialPlanRes)
    } catch (err) {
      console.error("Preview error:", err)
      setPreviewError(err instanceof Error ? err.message : "Failed to parse or analyze configuration file")
      setPreviewData(null)
      setParsedPayload(null)
    } finally {
      setIsPreviewing(false)
    }
  }

  // 3. Execute Import
  const handleExecuteImport = async () => {
    if (!parsedPayload) return

    setIsImporting(true)
    setPreviewError(null)

    try {
      const requestPayload = {
        payload: parsedPayload,
        userResolutions,
        agentResolutions,
        agentPlanMappings,
        planResolutions
      }

      const result = await apiPost<ImportResult>("/api/backup-restore/import", requestPayload)
      setImportResult(result)
    } catch (err) {
      console.error("Import execution error:", err)
      setPreviewError(err instanceof Error ? err.message : "Error executing import")
    } finally {
      setIsImporting(false)
    }
  }

  // Reset form
  const handleReset = () => {
    setSelectedFile(null)
    setParsedPayload(null)
    setPreviewData(null)
    setPreviewError(null)
    setImportResult(null)
    setUserResolutions({})
    setAgentResolutions({})
    setAgentPlanMappings({})
    setPlanResolutions({})
  }

  const hasConflicts = Boolean(
    previewData && (
      previewData.conflictedUsers.length > 0 ||
      previewData.conflictedAgents.length > 0 ||
      previewData.conflictedBackupPlans.length > 0
    )
  )

  return (
    <div className="space-y-6 max-w-5xl mx-auto">
      {/* Page Header */}
      <div>
        <h1 className="text-3xl font-bold flex items-center gap-3">
          <Archive className="h-8 w-8 text-primary" />
          Backup & Restore
        </h1>
        <p className="text-muted-foreground mt-1">
          Export and import system configurations including Users, Agents, SSH Keys, and Backup Plans.
        </p>
      </div>

      {/* Security & Scope Alert */}
      <div className="rounded-lg border border-blue-500/30 bg-blue-500/10 p-4 text-sm flex gap-3 items-start">
        <Info className="h-5 w-5 text-blue-500 shrink-0 mt-0.5" />
        <div className="space-y-1">
          <div className="font-semibold text-blue-900 dark:text-blue-200">
            Scope & Security Notice
          </div>
          <div className="text-blue-800 dark:text-blue-300">
            The exported configuration bundle includes <strong>Users</strong> (password hashes & preferences), <strong>Agents</strong> (hostnames, ports & SSH private keys), and <strong>Backup Plans</strong>. Logs and external extensions are omitted. Because this file contains private SSH credentials and password hashes, store it in a secure and encrypted location.
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {/* EXPORT CARD */}
        <div className="rounded-lg border bg-card p-6 flex flex-col justify-between shadow-sm">
          <div className="space-y-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-primary/10 text-primary">
                <Download className="h-6 w-6" />
              </div>
              <div>
                <h2 className="text-xl font-semibold">Export Configurations</h2>
                <p className="text-xs text-muted-foreground">Download current settings as JSON</p>
              </div>
            </div>

            <p className="text-sm text-muted-foreground">
              Generates a single JSON archive named <code className="bg-muted px-1 py-0.5 rounded font-mono text-xs">RemoteRSyncManager-yyyy-mm-dd-hh-mi-ss.json</code> containing all active agents, users, and backup plans.
            </p>

            <div className="space-y-2 pt-2">
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <CheckCircle2 className="h-4 w-4 text-green-500" />
                <span>Users with credentials & preferences</span>
              </div>
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <CheckCircle2 className="h-4 w-4 text-green-500" />
                <span>Agents with SSH private keys & tokens</span>
              </div>
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <CheckCircle2 className="h-4 w-4 text-green-500" />
                <span>Backup plans with schedules, sources & destinations</span>
              </div>
            </div>
          </div>

          <div className="pt-6">
            {exportError && (
              <div className="mb-3 text-sm text-destructive flex items-center gap-2">
                <AlertTriangle className="h-4 w-4" />
                <span>{exportError}</span>
              </div>
            )}
            <Button 
              onClick={handleExport} 
              disabled={isExporting} 
              className="w-full flex items-center justify-center gap-2"
            >
              {isExporting ? (
                <>
                  <RefreshCw className="h-4 w-4 animate-spin" />
                  Generating Export...
                </>
              ) : (
                <>
                  <Download className="h-4 w-4" />
                  Export Configurations
                </>
              )}
            </Button>
          </div>
        </div>

        {/* IMPORT CARD */}
        <div className="rounded-lg border bg-card p-6 flex flex-col justify-between shadow-sm">
          <div className="space-y-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-primary/10 text-primary">
                <Upload className="h-6 w-6" />
              </div>
              <div>
                <h2 className="text-xl font-semibold">Import Configurations</h2>
                <p className="text-xs text-muted-foreground">Merge settings from a JSON backup</p>
              </div>
            </div>

            <p className="text-sm text-muted-foreground">
              Upload a previously exported JSON file. You will be able to preview all changes and decide how to resolve any conflicts before committing.
            </p>

            <div className="pt-2">
              <Label htmlFor="backup-file" className="block text-sm font-medium mb-2">
                Select Configuration File (.json)
              </Label>
              <input
                id="backup-file"
                type="file"
                accept=".json,application/json"
                onChange={handleFileChange}
                disabled={isPreviewing || isImporting}
                className="block w-full text-sm text-muted-foreground file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-primary file:text-primary-foreground hover:file:opacity-90 cursor-pointer border rounded-md p-1"
              />
            </div>
          </div>

          {selectedFile && (
            <div className="pt-4 flex items-center justify-between text-xs text-muted-foreground">
              <span className="flex items-center gap-1.5 truncate">
                <FileJson className="h-4 w-4 text-primary shrink-0" />
                <span className="truncate">{selectedFile.name}</span>
              </span>
              <Button variant="ghost" size="sm" onClick={handleReset} className="h-7 text-xs">
                Clear
              </Button>
            </div>
          )}
        </div>
      </div>

      {/* ERROR BANNER */}
      {previewError && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive flex items-center gap-3">
          <AlertTriangle className="h-5 w-5 shrink-0" />
          <span>{previewError}</span>
        </div>
      )}

      {/* IMPORT SUCCESS RESULT */}
      {importResult && (
        <div className="rounded-lg border border-green-500/40 bg-green-500/10 p-6 space-y-4">
          <div className="flex items-center gap-3 text-green-700 dark:text-green-300 font-semibold text-lg">
            <CheckCircle2 className="h-6 w-6" />
            {importResult.message}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 pt-2">
            <div className="bg-background rounded-md p-3 border">
              <div className="text-xs text-muted-foreground font-medium">Users</div>
              <div className="text-sm font-semibold mt-1">
                <span className="text-green-600 dark:text-green-400">+{importResult.usersImported} imported</span>
                {importResult.usersUpdated > 0 && <span className="text-blue-500 ml-2">({importResult.usersUpdated} updated)</span>}
                {importResult.usersSkipped > 0 && <span className="text-muted-foreground ml-2">({importResult.usersSkipped} skipped)</span>}
              </div>
            </div>

            <div className="bg-background rounded-md p-3 border">
              <div className="text-xs text-muted-foreground font-medium">Agents</div>
              <div className="text-sm font-semibold mt-1">
                <span className="text-green-600 dark:text-green-400">+{importResult.agentsImported} imported</span>
                {importResult.agentsUpdated > 0 && <span className="text-blue-500 ml-2">({importResult.agentsUpdated} updated)</span>}
                {importResult.agentsSkipped > 0 && <span className="text-muted-foreground ml-2">({importResult.agentsSkipped} skipped)</span>}
              </div>
            </div>

            <div className="bg-background rounded-md p-3 border">
              <div className="text-xs text-muted-foreground font-medium">Backup Plans</div>
              <div className="text-sm font-semibold mt-1">
                <span className="text-green-600 dark:text-green-400">+{importResult.plansImported} imported</span>
                {importResult.plansUpdated > 0 && <span className="text-blue-500 ml-2">({importResult.plansUpdated} updated)</span>}
                {importResult.plansSkipped > 0 && <span className="text-muted-foreground ml-2">({importResult.plansSkipped} skipped)</span>}
              </div>
            </div>
          </div>

          <div className="pt-2">
            <Button onClick={handleReset} variant="outline" size="sm">
              Done / Import Another File
            </Button>
          </div>
        </div>
      )}

      {/* PREVIEW & CONFLICT RESOLUTION WIZARD */}
      {previewData && !importResult && (
        <div className="rounded-lg border bg-card p-6 space-y-6 shadow-sm">
          <div>
            <h2 className="text-xl font-bold flex items-center gap-2">
              <RefreshCw className="h-5 w-5 text-primary" />
              Import Analysis & Conflict Resolution
            </h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              Review items to be imported and choose how to resolve conflicts before executing the merge.
            </p>
          </div>

          {/* Quick Metrics */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div className="flex items-center justify-between p-3 rounded-lg border bg-muted/40">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Users className="h-4 w-4 text-blue-500" />
                <span>Users</span>
              </div>
              <div className="text-xs">
                <span className="text-green-600 dark:text-green-400 font-semibold">{previewData.newUsers.length} new</span>
                {previewData.conflictedUsers.length > 0 && (
                  <span className="text-amber-600 dark:text-amber-400 font-semibold ml-2">
                    {previewData.conflictedUsers.length} conflicts
                  </span>
                )}
              </div>
            </div>

            <div className="flex items-center justify-between p-3 rounded-lg border bg-muted/40">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Server className="h-4 w-4 text-purple-500" />
                <span>Agents</span>
              </div>
              <div className="text-xs">
                <span className="text-green-600 dark:text-green-400 font-semibold">{previewData.newAgents.length} new</span>
                {previewData.conflictedAgents.length > 0 && (
                  <span className="text-amber-600 dark:text-amber-400 font-semibold ml-2">
                    {previewData.conflictedAgents.length} conflicts
                  </span>
                )}
              </div>
            </div>

            <div className="flex items-center justify-between p-3 rounded-lg border bg-muted/40">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Database className="h-4 w-4 text-emerald-500" />
                <span>Backup Plans</span>
              </div>
              <div className="text-xs">
                <span className="text-green-600 dark:text-green-400 font-semibold">{previewData.newBackupPlans.length} new</span>
                {previewData.conflictedBackupPlans.length > 0 && (
                  <span className="text-amber-600 dark:text-amber-400 font-semibold ml-2">
                    {previewData.conflictedBackupPlans.length} conflicts
                  </span>
                )}
              </div>
            </div>
          </div>

          {!hasConflicts && (
            <div className="rounded-lg border border-green-500/30 bg-green-500/10 p-4 text-sm flex items-center gap-3">
              <CheckCircle2 className="h-5 w-5 text-green-500 shrink-0" />
              <span>
                <strong>No conflicts detected!</strong> All {previewData.newUsers.length} users, {previewData.newAgents.length} agents, and {previewData.newBackupPlans.length} backup plans will be cleanly imported into the database.
              </span>
            </div>
          )}

          {/* USER CONFLICTS */}
          {previewData.conflictedUsers.length > 0 && (
            <div className="space-y-4 pt-2">
              <div className="flex items-center gap-2 text-base font-semibold border-b pb-2">
                <Users className="h-5 w-5 text-blue-500" />
                <span>User Conflicts ({previewData.conflictedUsers.length})</span>
              </div>

              <div className="space-y-3">
                {previewData.conflictedUsers.map((conflict) => {
                  const currentRes = userResolutions[conflict.imported.id] || "keep_existing"
                  return (
                    <div key={conflict.imported.id} className="rounded-lg border p-4 space-y-3 bg-muted/20">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2 text-sm font-medium">
                          <AlertTriangle className="h-4 w-4 text-amber-500" />
                          <span>Conflict on user: <strong>{conflict.imported.username}</strong></span>
                        </div>
                        <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded">
                          {conflict.reason}
                        </span>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-foreground">Local / Existing:</span>
                          <div>Username: {conflict.existing.username}</div>
                          <div>Email: {conflict.existing.email}</div>
                          <div>Role: {conflict.existing.isAdmin ? "Administrator" : "Standard User"}</div>
                        </div>

                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-primary">Imported:</span>
                          <div>Username: {conflict.imported.username}</div>
                          <div>Email: {conflict.imported.email}</div>
                          <div>Role: {conflict.imported.isAdmin ? "Administrator" : "Standard User"}</div>
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center gap-4 text-xs pt-1">
                        <span className="font-medium text-foreground">Action:</span>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`user-action-${conflict.imported.id}`}
                            checked={currentRes === "keep_existing"}
                            onChange={() => setUserResolutions({ ...userResolutions, [conflict.imported.id]: "keep_existing" })}
                          />
                          <span>Keep Local (Preserve existing)</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`user-action-${conflict.imported.id}`}
                            checked={currentRes === "overwrite"}
                            onChange={() => setUserResolutions({ ...userResolutions, [conflict.imported.id]: "overwrite" })}
                          />
                          <span className="text-amber-600 dark:text-amber-400 font-medium">Overwrite Local</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`user-action-${conflict.imported.id}`}
                            checked={currentRes === "skip"}
                            onChange={() => setUserResolutions({ ...userResolutions, [conflict.imported.id]: "skip" })}
                          />
                          <span>Skip</span>
                        </label>
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* AGENT CONFLICTS */}
          {previewData.conflictedAgents.length > 0 && (
            <div className="space-y-4 pt-4">
              <div className="flex items-center gap-2 text-base font-semibold border-b pb-2">
                <Server className="h-5 w-5 text-purple-500" />
                <span>Agent Conflicts ({previewData.conflictedAgents.length})</span>
              </div>

              <div className="space-y-4">
                {previewData.conflictedAgents.map((conflict) => {
                  const currentRes = agentResolutions[conflict.imported.id] || "keep_existing"
                  const targetAgentId = agentPlanMappings[conflict.imported.id] || conflict.existing.id

                  return (
                    <div key={conflict.imported.id} className="rounded-lg border p-4 space-y-3.5 bg-muted/20">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2 text-sm font-medium">
                          <AlertTriangle className="h-4 w-4 text-amber-500" />
                          <span>Conflict on agent: <strong>{conflict.imported.name}</strong> ({conflict.imported.hostname})</span>
                        </div>
                        <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded">
                          {conflict.reason}
                        </span>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-foreground">Local / Existing:</span>
                          <div>Name: {conflict.existing.name}</div>
                          <div>Host: {conflict.existing.hostname}:{conflict.existing.rsyncPort}</div>
                          <div>User: {conflict.existing.rsyncUser || "root"}</div>
                        </div>

                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-primary">Imported:</span>
                          <div>Name: {conflict.imported.name}</div>
                          <div>Host: {conflict.imported.hostname}:{conflict.imported.rsyncPort}</div>
                          <div>User: {conflict.imported.rsyncUser || "root"}</div>
                        </div>
                      </div>

                      {/* Action selector */}
                      <div className="flex flex-wrap items-center gap-4 text-xs pt-1">
                        <span className="font-medium text-foreground">Action:</span>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`agent-action-${conflict.imported.id}`}
                            checked={currentRes === "keep_existing"}
                            onChange={() => setAgentResolutions({ ...agentResolutions, [conflict.imported.id]: "keep_existing" })}
                          />
                          <span>Keep Local (Preserve existing)</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`agent-action-${conflict.imported.id}`}
                            checked={currentRes === "overwrite"}
                            onChange={() => setAgentResolutions({ ...agentResolutions, [conflict.imported.id]: "overwrite" })}
                          />
                          <span className="text-amber-600 dark:text-amber-400 font-medium">Overwrite Local</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`agent-action-${conflict.imported.id}`}
                            checked={currentRes === "create_new"}
                            onChange={() => setAgentResolutions({ ...agentResolutions, [conflict.imported.id]: "create_new" })}
                          />
                          <span className="text-blue-500 font-medium">Create as New Agent</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`agent-action-${conflict.imported.id}`}
                            checked={currentRes === "skip"}
                            onChange={() => setAgentResolutions({ ...agentResolutions, [conflict.imported.id]: "skip" })}
                          />
                          <span>Skip</span>
                        </label>
                      </div>

                      {/* Plan association selector */}
                      {conflict.associatedPlans.length > 0 && (
                        <div className="rounded-md bg-blue-50/50 dark:bg-blue-950/20 border border-blue-200 dark:border-blue-900/50 p-3 space-y-2">
                          <div className="text-xs font-semibold text-blue-900 dark:text-blue-200 flex items-center gap-1.5">
                            <Database className="h-4 w-4" />
                            <span>
                              Associated Backup Plans ({conflict.associatedPlans.length}):
                            </span>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            Plans in backup: {conflict.associatedPlans.map(p => p.name).join(", ")}.
                            Which agent should these backup plans be assigned to?
                          </p>
                          <div className="flex items-center gap-3 pt-1">
                            <Label htmlFor={`plan-target-${conflict.imported.id}`} className="text-xs whitespace-nowrap">
                              Assign plans to:
                            </Label>
                            <select
                              id={`plan-target-${conflict.imported.id}`}
                              value={targetAgentId}
                              onChange={(e) => setAgentPlanMappings({ ...agentPlanMappings, [conflict.imported.id]: e.target.value })}
                              className="text-xs border rounded px-2.5 py-1.5 bg-background text-foreground max-w-sm"
                            >
                              <option value={conflict.existing.id}>
                                Matching Local Agent: {conflict.existing.name} ({conflict.existing.hostname})
                              </option>
                              {previewData.existingAgents
                                .filter(a => a.id !== conflict.existing.id)
                                .map(a => (
                                  <option key={a.id} value={a.id}>
                                    {a.name} ({a.hostname})
                                  </option>
                                ))}
                            </select>
                          </div>
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* BACKUP PLAN CONFLICTS */}
          {previewData.conflictedBackupPlans.length > 0 && (
            <div className="space-y-4 pt-4">
              <div className="flex items-center gap-2 text-base font-semibold border-b pb-2">
                <Database className="h-5 w-5 text-emerald-500" />
                <span>Backup Plan Conflicts ({previewData.conflictedBackupPlans.length})</span>
              </div>

              <div className="space-y-3">
                {previewData.conflictedBackupPlans.map((conflict) => {
                  const currentRes = planResolutions[conflict.imported.id] || "keep_existing"
                  return (
                    <div key={conflict.imported.id} className="rounded-lg border p-4 space-y-3 bg-muted/20">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2 text-sm font-medium">
                          <AlertTriangle className="h-4 w-4 text-amber-500" />
                          <span>Conflict on backup plan: <strong>{conflict.imported.name}</strong></span>
                        </div>
                        <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded">
                          {conflict.reason}
                        </span>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-foreground">Local / Existing:</span>
                          <div>Name: {conflict.existing.name}</div>
                          <div>Schedule: {conflict.existing.schedule}</div>
                          <div>Source: {conflict.existing.source}</div>
                          <div>Destination: {conflict.existing.destination}</div>
                          <div>Agent: {conflict.existing.agentName || "None"}</div>
                        </div>

                        <div className="rounded border bg-background p-2.5 space-y-1">
                          <span className="font-semibold text-primary">Imported:</span>
                          <div>Name: {conflict.imported.name}</div>
                          <div>Schedule: {conflict.imported.schedule}</div>
                          <div>Source: {conflict.imported.source}</div>
                          <div>Destination: {conflict.imported.destination}</div>
                          <div>Agent: {conflict.imported.agentName || "None"}</div>
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center gap-4 text-xs pt-1">
                        <span className="font-medium text-foreground">Action:</span>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`plan-action-${conflict.imported.id}`}
                            checked={currentRes === "keep_existing"}
                            onChange={() => setPlanResolutions({ ...planResolutions, [conflict.imported.id]: "keep_existing" })}
                          />
                          <span>Keep Local (Preserve existing)</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`plan-action-${conflict.imported.id}`}
                            checked={currentRes === "overwrite"}
                            onChange={() => setPlanResolutions({ ...planResolutions, [conflict.imported.id]: "overwrite" })}
                          />
                          <span className="text-amber-600 dark:text-amber-400 font-medium">Overwrite Local</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`plan-action-${conflict.imported.id}`}
                            checked={currentRes === "create_new"}
                            onChange={() => setPlanResolutions({ ...planResolutions, [conflict.imported.id]: "create_new" })}
                          />
                          <span className="text-blue-500 font-medium">Create as New Plan</span>
                        </label>
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input
                            type="radio"
                            name={`plan-action-${conflict.imported.id}`}
                            checked={currentRes === "skip"}
                            onChange={() => setPlanResolutions({ ...planResolutions, [conflict.imported.id]: "skip" })}
                          />
                          <span>Skip</span>
                        </label>
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* Commit Button */}
          <div className="pt-4 border-t flex items-center justify-between">
            <Button variant="outline" onClick={handleReset} disabled={isImporting}>
              Cancel
            </Button>
            <Button onClick={handleExecuteImport} disabled={isImporting} className="gap-2">
              {isImporting ? (
                <>
                  <RefreshCw className="h-4 w-4 animate-spin" />
                  Applying Merge...
                </>
              ) : (
                <>
                  <ArrowRight className="h-4 w-4" />
                  Execute Import & Merge
                </>
              )}
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
