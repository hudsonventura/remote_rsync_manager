import { useState, useEffect } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ArrowLeft } from "lucide-react"
import { apiGet, apiPost } from "@/lib/api"

interface Agent {
  id: string
  hostname: string
}

export function AddBackupPlan() {
  const navigate = useNavigate()
  const { agentId } = useParams()
  const [agent, setAgent] = useState<Agent | null>(null)
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [schedule, setSchedule] = useState("0 0 * * *")
  const [source, setSource] = useState("")
  const [destination, setDestination] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [isLoadingAgent, setIsLoadingAgent] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchAgent = async () => {
      if (!agentId) {
        setError("Agent ID is required")
        setIsLoadingAgent(false)
        return
      }

      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        const data: Agent = await apiGet<Agent>(`/api/agent/${agentId}`)
        setAgent(data)
      } catch (err) {
        setError(err instanceof Error ? err.message : "An error occurred")
      } finally {
        setIsLoadingAgent(false)
      }
    }

    fetchAgent()
  }, [agentId, navigate])

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

      if (!agentId) {
        setError("Agent ID is required")
        setIsLoading(false)
        return
      }

      await apiPost("/api/backupplan", {
        name: name.trim(),
        description: description.trim(),
        schedule: schedule.trim() || "0 0 * * *",
        source: source.trim(),
        destination: destination.trim(),
        agentId: agentId,
      })

      // Redirect to backup plans page
      navigate(`/agents/${agentId}/backup-plans`)
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

  if (isLoadingAgent) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/agents")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Agents
          </Button>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading agent information...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate(`/agents/${agentId}/backup-plans`)}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-3xl font-bold">Add Backup Plan</h1>
            {agent && (
              <p className="text-muted-foreground mt-2">
                For agent: <span className="font-medium">{agent.hostname}</span>
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
          </div>

          <div className="space-y-2">
            <Label htmlFor="source">Source Path *</Label>
            <Input
              id="source"
              type="text"
              placeholder="/path/to/source"
              value={source}
              onChange={(e) => setSource(e.target.value)}
              required
              disabled={isLoading}
            />
            <p className="text-sm text-muted-foreground">
              Path to the files/directories to backup
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="destination">Destination Path *</Label>
            <Input
              id="destination"
              type="text"
              placeholder="/path/to/destination"
              value={destination}
              onChange={(e) => setDestination(e.target.value)}
              required
              disabled={isLoading}
            />
            <p className="text-sm text-muted-foreground">
              Path where the backup will be stored
            </p>
          </div>

          <div className="flex gap-4">
            <Button type="submit" disabled={isLoading}>
              {isLoading ? "Creating..." : "Create Backup Plan"}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate(`/agents/${agentId}/backup-plans`)}
              disabled={isLoading}
            >
              Cancel
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

