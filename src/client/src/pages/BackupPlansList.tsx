import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Plus, Pencil } from "lucide-react"
import { apiGet } from "@/lib/api"

interface BackupPlan {
  id: string
  name: string
  description: string
  schedule: string
  source: string
  destination: string
  agentId?: string
  agentHostname?: string
}

export function BackupPlansList() {
  const navigate = useNavigate()
  const [backupPlans, setBackupPlans] = useState<BackupPlan[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchData = async () => {
      setIsLoading(true)
      setError(null)

      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        // Fetch all backup plans
        const plansData: BackupPlan[] = await apiGet<BackupPlan[]>("/api/backupplan")
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
  }, [navigate])

  const getAgentHostname = (plan: BackupPlan) => {
    if (plan.agentHostname) {
      return plan.agentHostname
    }
    if (plan.agentId) {
      return "Unknown Agent"
    }
    return "No Agent Assigned"
  }

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold">Backup Plans</h1>
            <p className="text-muted-foreground mt-2">
              View and manage all backup plans
            </p>
          </div>
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
        <div>
          <h1 className="text-3xl font-bold">Backup Plans</h1>
          <p className="text-muted-foreground mt-2">
            View and manage all backup plans across all agents
          </p>
        </div>
        <Button onClick={() => navigate("/agents")}>
          <Plus className="h-4 w-4 mr-2" />
          Add via Agent
        </Button>
      </div>

      {error && (
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {!error && backupPlans.length === 0 && (
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="text-center py-12">
            <p className="text-muted-foreground mb-4">No backup plans found</p>
            <p className="text-sm text-muted-foreground mb-4">
              Create backup plans by navigating to an agent
            </p>
            <Button onClick={() => navigate("/agents")}>
              View Agents
            </Button>
          </div>
        </div>
      )}

      {!error && backupPlans.length > 0 && (
        <div className="space-y-4">
          {backupPlans.map((plan) => (
            <div
              key={plan.id}
              className="rounded-lg border bg-card p-6 shadow-sm hover:shadow-md transition-shadow cursor-pointer"
              onClick={() => {
                if (plan.agentId) {
                  navigate(`/agents/${plan.agentId}/backup-plans`)
                }
              }}
            >
              <div className="flex items-start justify-between">
                <div className="space-y-2 flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    <h3 className="text-xl font-semibold">{plan.name}</h3>
                  </div>
                  <div className="mb-2">
                    <p className="text-sm font-medium text-muted-foreground">Agent</p>
                    <p className="text-base font-medium">{getAgentHostname(plan)}</p>
                  </div>
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
                      <p className="text-sm truncate" title={plan.source}>{plan.source}</p>
                    </div>
                    <div className="md:col-span-2">
                      <p className="text-sm font-medium text-muted-foreground">Destination</p>
                      <p className="text-sm truncate" title={plan.destination}>{plan.destination}</p>
                    </div>
                  </div>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={(e) => {
                    e.stopPropagation()
                    navigate(`/backup-plans/${plan.id}/edit${plan.agentId ? `?agentId=${plan.agentId}` : ''}`)
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

