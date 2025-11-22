import { useState, useEffect } from "react"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ArrowLeft, Trash2, FolderOpen, Play, Zap } from "lucide-react"
import { apiGet, apiPut, apiDelete, apiPost } from "@/lib/api"
import { FileBrowser } from "@/components/FileBrowser"
import { ServerFileBrowser } from "@/components/ServerFileBrowser"
import { CronDescription } from "@/components/CronDescription"
import { SimulationResults } from "@/components/SimulationResults"
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

interface BackupPlan {
  id: string
  name: string
  description: string
  schedule: string
  source: string
  destination: string
  active?: boolean
  agentid?: string
}

interface Agent {
  id: string
  name: string
  hostname: string
}

export function EditBackupPlan() {
  const navigate = useNavigate()
  const { planId, agentId: agentIdFromPath } = useParams()
  const [searchParams] = useSearchParams()
  const agentIdFromQuery = searchParams.get("agentId")
  const agentId = agentIdFromPath || agentIdFromQuery || undefined
  const [agent, setAgent] = useState<Agent | null>(null)
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [schedule, setSchedule] = useState("0 0 * * *")
  const [source, setSource] = useState("")
  const [destination, setDestination] = useState("")
  const [active, setActive] = useState(true)
  const [isLoading, setIsLoading] = useState(false)
  const [isLoadingData, setIsLoadingData] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showFileBrowser, setShowFileBrowser] = useState(false)
  const [showServerFileBrowser, setShowServerFileBrowser] = useState(false)
  const [showSimulation, setShowSimulation] = useState(false)
  const [simulationResult, setSimulationResult] = useState<any>(null)
  const [isSimulating, setIsSimulating] = useState(false)
  const [isExecuting, setIsExecuting] = useState(false)
  const [executionMessage, setExecutionMessage] = useState<string | null>(null)

  useEffect(() => {
    const fetchData = async () => {
      if (!planId) {
        setError("Backup plan ID is required")
        setIsLoadingData(false)
        return
      }

      setIsLoadingData(true)
      setError(null)

      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        // Fetch backup plan
        const planData: BackupPlan = await apiGet<BackupPlan>(`/api/backupplan/${planId}`)
        setName(planData.name)
        setDescription(planData.description)
        setSchedule(planData.schedule)
        setSource(planData.source)
        setDestination(planData.destination)
        setActive(planData.active ?? false)

        // Fetch agent if agentId is provided or from plan
        const agentIdToFetch = agentId || planData.agentid
        if (agentIdToFetch) {
          try {
            const agentData: Agent = await apiGet<Agent>(`/api/agent/${agentIdToFetch}`)
            setAgent(agentData)
          } catch {
            // Agent fetch failed, but continue without agent info
          }
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "An error occurred")
      } finally {
        setIsLoadingData(false)
      }
    }

    fetchData()
  }, [planId, agentId, navigate])

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setError(null)
    setIsLoading(true)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      if (!planId) {
        setError("Backup plan ID is required")
        setIsLoading(false)
        return
      }

      await apiPut(`/api/backupplan/${planId}`, {
        name: name.trim(),
        description: description.trim(),
        schedule: schedule.trim() || "0 0 * * *",
        source: source.trim(),
        destination: destination.trim(),
        active: active,
      })

      // Redirect back to the appropriate page
      const redirectPath = agentIdFromPath 
        ? `/agents/${agentIdFromPath}/backup-plans`
        : agentIdFromQuery
        ? `/agents/${agentIdFromQuery}/backup-plans`
        : "/backup-plans"
      navigate(redirectPath)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred")
      }
    } finally {
      setIsLoading(false)
    }
  }

  const handleDelete = async () => {
    if (!planId) {
      setError("Backup plan ID is required")
      return
    }

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiDelete(`/api/backupplan/${planId}`)
      // Redirect back to the appropriate page
      navigate(getBackPath())
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while deleting the backup plan")
      }
    }
  }

  const getBackPath = () => {
    if (agentIdFromPath) return `/agents/${agentIdFromPath}/backup-plans`
    if (agentIdFromQuery) return `/agents/${agentIdFromQuery}/backup-plans`
    return "/backup-plans"
  }

  const handleSimulate = async () => {
    if (!planId) {
      setError("Backup plan ID is required")
      return
    }

    setIsSimulating(true)
    setError(null)
    setSimulationResult(null)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      const result = await apiPost(`/api/backupplan/${planId}/simulate`, {})
      setSimulationResult(result)
      setShowSimulation(true)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while simulating the backup plan")
      }
    } finally {
      setIsSimulating(false)
    }
  }

  const handleExecute = async () => {
    if (!planId) {
      setError("Backup plan ID is required")
      return
    }

    setIsExecuting(true)
    setError(null)
    setExecutionMessage(null)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiPost(`/api/backupplan/${planId}/execute`, {})
      setExecutionMessage("Backup plan execution started. The backup will run in the background.")
      
      // Clear the message after 5 seconds
      setTimeout(() => {
        setExecutionMessage(null)
      }, 5000)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while executing the backup plan")
      }
    } finally {
      setIsExecuting(false)
    }
  }

  if (isLoadingData) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate(getBackPath())}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back
          </Button>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading backup plan...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate(getBackPath())}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-3xl font-bold">Edit Backup Plan</h1>
            {agent && (
              <p className="text-muted-foreground mt-2">
                For agent: <span className="font-medium">{agent.name}</span>
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

      {executionMessage && (
        <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
          {executionMessage}
        </div>
      )}

      <div className="rounded-lg border bg-card p-6 shadow-sm max-w-3xl">
        <form onSubmit={handleSubmit} className="space-y-6">
          <div className="space-y-2">
            <Label htmlFor="name">Name *</Label>
            <Input
              id="name"
              type="text"
              placeholder="Daily Backup"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              disabled={isLoading}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Input
              id="description"
              type="text"
              placeholder="Backup important files"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              disabled={isLoading}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="schedule">Schedule (Cron) *</Label>
            <Input
              id="schedule"
              type="text"
              placeholder="0 0 * * *"
              value={schedule}
              onChange={(e) => setSchedule(e.target.value)}
              required
              disabled={isLoading}
            />
            <p className="text-sm text-muted-foreground">
              Cron expression (e.g., "0 0 * * *" for daily at midnight)
            </p>
            <CronDescription cronExpression={schedule} />
          </div>

          <div className="space-y-2">
            <Label htmlFor="source">Source Path  (Agent)</Label>
            <div className="flex gap-2">
              <Input
                id="source"
                type="text"
                placeholder="/path/to/source"
                value={source}
                onChange={(e) => setSource(e.target.value)}
                required
                disabled={isLoading}
                className="flex-1"
              />
              {agentId && (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setShowFileBrowser(true)}
                  disabled={isLoading}
                  title="Browse file system on agent"
                >
                  <FolderOpen className="h-4 w-4" />
                </Button>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              Path to the files/directories to backup
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="destination">Destination Path (Server)</Label>
            <div className="flex gap-2">
              <Input
                id="destination"
                type="text"
                placeholder="/path/to/destination"
                value={destination}
                onChange={(e) => setDestination(e.target.value)}
                required
                disabled={isLoading}
                className="flex-1"
              />
              <Button
                type="button"
                variant="outline"
                onClick={() => setShowServerFileBrowser(true)}
                disabled={isLoading}
                title="Browse server file system"
              >
                <FolderOpen className="h-4 w-4" />
              </Button>
            </div>
            <p className="text-sm text-muted-foreground">
              Path where the backup will be stored on the server
            </p>
          </div>

          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <input
                id="active"
                type="checkbox"
                checked={active}
                onChange={(e) => setActive(e.target.checked)}
                disabled={isLoading}
                className="h-4 w-4 rounded border-gray-300"
              />
              <Label htmlFor="active" className="cursor-pointer">
                Active
              </Label>
            </div>
            <p className="text-sm text-muted-foreground">
              Only active backup plans will be executed according to their schedule
            </p>
          </div>

              <div className="flex gap-4">
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleSimulate}
                  disabled={isLoading || isSimulating || isExecuting}
                >
                  <Play className="h-4 w-4 mr-2" />
                  {isSimulating ? "Simulating..." : "Simulate"}
                </Button>
                <AlertDialog>
                  <AlertDialogTrigger asChild>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={isLoading || isSimulating || isExecuting}
                    >
                      <Zap className="h-4 w-4 mr-2" />
                      Execute Now
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Execute Backup Plan</AlertDialogTitle>
                      <AlertDialogDescription>
                        Are you sure you want to execute the backup plan <strong>{name}</strong> now?
                        This will start the backup process immediately.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction
                        onClick={handleExecute}
                        className="bg-primary text-primary-foreground hover:bg-primary/90"
                      >
                        Execute
                      </AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
                <Button type="submit" disabled={isLoading}>
                  {isLoading ? "Saving..." : "Save Changes"}
                </Button>
                <AlertDialog>
                  <AlertDialogTrigger asChild>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={isLoading}
                      className="text-destructive hover:text-destructive"
                    >
                      <Trash2 className="h-4 w-4 mr-2" />
                      Delete
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Are you sure?</AlertDialogTitle>
                      <AlertDialogDescription>
                        This action cannot be undone. This will permanently delete the backup plan
                        <strong> {name}</strong>.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction
                        onClick={handleDelete}
                        className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                      >
                        Delete
                      </AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => navigate(getBackPath())}
                  disabled={isLoading}
                >
                  Cancel
                </Button>
              </div>
        </form>
      </div>

      {agentId && (
        <FileBrowser
          agentId={agentId}
          open={showFileBrowser}
          onClose={() => setShowFileBrowser(false)}
          onSelect={(path) => {
            setSource(path)
            setShowFileBrowser(false)
          }}
          initialPath={source}
        />
      )}

      <ServerFileBrowser
        open={showServerFileBrowser}
        onClose={() => setShowServerFileBrowser(false)}
        onSelect={(path) => {
          setDestination(path)
          setShowServerFileBrowser(false)
        }}
        initialPath={destination}
      />

      <SimulationResults
        open={showSimulation}
        onClose={() => setShowSimulation(false)}
        result={simulationResult}
        isLoading={isSimulating}
      />
    </div>
  )
}

