import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Plus, Pencil, Trash2, FileText } from "lucide-react"
import { apiGet, apiDelete } from "@/lib/api"
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

interface BackupPlan {
  id: string
  name: string
  description: string
  schedule: string
  source: string
  destination: string
  active?: boolean
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

  const handleDelete = async (planId: string, planName: string) => {
    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiDelete(`/api/backupplan/${planId}`)
      // Refresh the list
      const plansData: BackupPlan[] = await apiGet<BackupPlan[]>("/api/backupplan")
      setBackupPlans(plansData)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while deleting the backup plan")
      }
    }
  }

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
                    <span className={`px-2 py-1 text-xs font-medium rounded ${
                      plan.active !== false 
                        ? "bg-green-500/20 text-green-600 dark:text-green-400" 
                        : "bg-gray-500/20 text-gray-600 dark:text-gray-400"
                    }`}>
                      {plan.active !== false ? "Active" : "Inactive"}
                    </span>
                  </div>
                  <div className="mb-2">
                    <p className="text-sm font-medium text-muted-foreground">Agent</p>
                    <p className="text-base font-medium">{getAgentHostname(plan)}</p>
                  </div>
                  {plan.description && (
                    <p className="text-muted-foreground">{plan.description}</p>
                  )}
                  <div className="space-y-4 mt-4">
                    <div>
                      <p className="text-sm font-medium text-muted-foreground mb-2">Schedule</p>
                      <p className="text-sm font-mono mb-2">{plan.schedule}</p>
                      <CronDescription cronExpression={plan.schedule} />
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <p className="text-sm font-medium text-muted-foreground">Source</p>
                        <p className="text-sm truncate" title={plan.source}>{plan.source}</p>
                      </div>
                      <div>
                        <p className="text-sm font-medium text-muted-foreground">Destination</p>
                        <p className="text-sm truncate" title={plan.destination}>{plan.destination}</p>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="flex gap-2 ml-4">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation()
                      navigate(`/backup-plans/${plan.id}/logs`)
                    }}
                  >
                    <FileText className="h-4 w-4 mr-2" />
                    Logs
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation()
                      navigate(`/backup-plans/${plan.id}/edit${plan.agentId ? `?agentId=${plan.agentId}` : ''}`)
                    }}
                  >
                  <Pencil className="h-4 w-4 mr-2" />
                    Edit
                  </Button>
                  <AlertDialog>
                    <AlertDialogTrigger asChild>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={(e) => e.stopPropagation()}
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
                          <strong> {plan.name}</strong>.
                        </AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>Cancel</AlertDialogCancel>
                        <AlertDialogAction
                          onClick={() => handleDelete(plan.id, plan.name)}
                          className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                        >
                          Delete
                        </AlertDialogAction>
                      </AlertDialogFooter>
                    </AlertDialogContent>
                  </AlertDialog>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

