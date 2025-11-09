import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { ArrowLeft, Plus, Pencil } from "lucide-react"

const API_URL = import.meta.env.VITE_API_URL || "http://localhost:5000"

interface BackupPlan {
  id: string
  name: string
  description: string
  schedule: string
  source: string
  destination: string
}

interface Agent {
  id: string
  hostname: string
}

export function AgentBackupPlans() {
  const navigate = useNavigate()
  const { agentId } = useParams<{ agentId: string }>()
  const [agent, setAgent] = useState<Agent | null>(null)
  const [backupPlans, setBackupPlans] = useState<BackupPlan[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchData = async () => {
      if (!agentId) {
        setError("Agent ID is required")
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

        // Fetch agent details
        const agentResponse = await fetch(`${API_URL}/api/agent/${agentId}`, {
          method: "GET",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${token}`,
          },
        })

        if (!agentResponse.ok) {
          if (agentResponse.status === 404) {
            setError("Agent not found")
            setIsLoading(false)
            return
          }
          throw new Error("Failed to fetch agent")
        }

        const agentData: Agent = await agentResponse.json()
        setAgent(agentData)

        // Fetch backup plans for the agent
        const plansResponse = await fetch(`${API_URL}/api/backupplan/agent/${agentId}`, {
          method: "GET",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${token}`,
          },
        })

        if (!plansResponse.ok) {
          throw new Error("Failed to fetch backup plans")
        }

        const plansData: BackupPlan[] = await plansResponse.json()
        setBackupPlans(plansData)
      } catch (err) {
        if (err instanceof TypeError && err.message === "Failed to fetch") {
          setError("Unable to connect to the server. Please make sure the backend is running.")
        } else {
          setError(err instanceof Error ? err.message : "An error occurred while fetching data")
        }
      } finally {
        setIsLoading(false)
      }
    }

    fetchData()
  }, [agentId, navigate])

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/agents")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Agents
          </Button>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading backup plans...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" onClick={() => navigate("/agents")}>
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back to Agents
          </Button>
          <div>
            <h1 className="text-3xl font-bold">
              Backup Plans
              {agent && (
                <span className="text-muted-foreground text-xl ml-2">
                  - {agent.hostname}
                </span>
              )}
            </h1>
            <p className="text-muted-foreground mt-2">
              Manage backup plans for this agent
            </p>
          </div>
        </div>
        {agentId && (
          <Button onClick={() => navigate(`/agents/${agentId}/backup-plans/add`)}>
            <Plus className="h-4 w-4 mr-2" />
            Add Backup Plan
          </Button>
        )}
      </div>

      {error && (
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {!error && backupPlans.length === 0 && (
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="text-center py-12">
            <p className="text-muted-foreground mb-4">No backup plans found for this agent</p>
            <p className="text-sm text-muted-foreground">
              Create a backup plan to get started
            </p>
          </div>
        </div>
      )}

      {!error && backupPlans.length > 0 && (
        <div className="space-y-4">
          {backupPlans.map((plan) => (
            <div
              key={plan.id}
              className="rounded-lg border bg-card p-6 shadow-sm"
            >
              <div className="flex items-start justify-between">
                <div className="space-y-2 flex-1">
                  <h3 className="text-xl font-semibold">{plan.name}</h3>
                  {plan.description && (
                    <p className="text-muted-foreground">{plan.description}</p>
                  )}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
                    <div>
                      <p className="text-sm font-medium text-muted-foreground">Schedule</p>
                      <p className="text-sm font-mono">{plan.schedule}</p>
                    </div>
                    <div>
                      <p className="text-sm font-medium text-muted-foreground">Source</p>
                      <p className="text-sm">{plan.source}</p>
                    </div>
                    <div className="md:col-span-2">
                      <p className="text-sm font-medium text-muted-foreground">Destination</p>
                      <p className="text-sm">{plan.destination}</p>
                    </div>
                  </div>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={(e) => {
                    e.stopPropagation()
                    navigate(`/agents/${agentId}/backup-plans/${plan.id}/edit`)
                  }}
                  className="ml-4"
                >
                  <Pencil className="h-4 w-4 mr-2" />
                  Edit
                </Button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

