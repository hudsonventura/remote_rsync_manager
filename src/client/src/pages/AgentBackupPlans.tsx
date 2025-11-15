import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { ArrowLeft, Plus, Pencil, Trash2, FileText, Play, Zap } from "lucide-react"
import { apiGet, apiDelete, apiPost } from "@/lib/api"
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
  const [simulatingPlanId, setSimulatingPlanId] = useState<string | null>(null)
  const [executingPlanId, setExecutingPlanId] = useState<string | null>(null)
  const [showSimulation, setShowSimulation] = useState(false)
  const [simulationResult, setSimulationResult] = useState<any>(null)
  const [executionMessages, setExecutionMessages] = useState<Record<string, string>>({})

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
        const agentData: Agent = await apiGet<Agent>(`/api/agent/${agentId}`)
        setAgent(agentData)

        // Fetch backup plans for the agent
        const plansData: BackupPlan[] = await apiGet<BackupPlan[]>(`/api/backupplan/agent/${agentId}`)
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

  const handleDelete = async (planId: string, planName: string) => {
    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiDelete(`/api/backupplan/${planId}`)
      // Refresh the list
      const plansData: BackupPlan[] = await apiGet<BackupPlan[]>(`/api/backupplan/agent/${agentId}`)
      setBackupPlans(plansData)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while deleting the backup plan")
      }
    }
  }

  const handleSimulate = async (planId: string) => {
    setSimulatingPlanId(planId)
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
      setSimulatingPlanId(null)
    }
  }

  const handleExecute = async (planId: string) => {
    setExecutingPlanId(planId)
    setError(null)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiPost(`/api/backupplan/${planId}/execute`, {})
      setExecutionMessages(prev => ({
        ...prev,
        [planId]: "Backup plan execution started. The backup will run in the background."
      }))
      
      // Clear the message after 5 seconds
      setTimeout(() => {
        setExecutionMessages(prev => {
          const newMessages = { ...prev }
          delete newMessages[planId]
          return newMessages
        })
      }, 5000)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while executing the backup plan")
      }
    } finally {
      setExecutingPlanId(null)
    }
  }

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

      {Object.entries(executionMessages).map(([planId, message]) => (
        <div key={planId} className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
          {message}
        </div>
      ))}

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
              <div className="space-y-4">
                <div className="flex items-start justify-between">
                  <div className="space-y-2 flex-1">
                    <div className="flex items-center gap-3">
                      <h3 className="text-xl font-semibold">{plan.name}</h3>
                      <span className={`px-2 py-1 text-xs font-medium rounded ${
                        plan.active !== false 
                          ? "bg-green-500/20 text-green-600 dark:text-green-400" 
                          : "bg-gray-500/20 text-gray-600 dark:text-gray-400"
                      }`}>
                        {plan.active !== false ? "Active" : "Inactive"}
                      </span>
                    </div>
                    {plan.description && (
                      <p className="text-muted-foreground">{plan.description}</p>
                    )}
                    <div className="space-y-4 mt-4">
                      <div>
                        <p className="text-sm font-medium text-muted-foreground">Schedule</p>
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
                        navigate(`/agents/${agentId}/backup-plans/${plan.id}/edit`)
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
                <div className="flex justify-end gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation()
                      handleSimulate(plan.id)
                    }}
                    disabled={simulatingPlanId === plan.id || executingPlanId === plan.id}
                  >
                    <Play className="h-4 w-4 mr-2" />
                    {simulatingPlanId === plan.id ? "Simulating..." : "Simulate"}
                  </Button>
                  <AlertDialog>
                    <AlertDialogTrigger asChild>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={(e) => e.stopPropagation()}
                        disabled={simulatingPlanId === plan.id || executingPlanId === plan.id}
                      >
                        <Zap className="h-4 w-4 mr-2" />
                        Execute Now
                      </Button>
                    </AlertDialogTrigger>
                    <AlertDialogContent>
                      <AlertDialogHeader>
                        <AlertDialogTitle>Execute Backup Plan</AlertDialogTitle>
                        <AlertDialogDescription>
                          Are you sure you want to execute the backup plan <strong>{plan.name}</strong> now?
                          This will start the backup process immediately.
                        </AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>Cancel</AlertDialogCancel>
                        <AlertDialogAction
                          onClick={() => handleExecute(plan.id)}
                          className="bg-primary text-primary-foreground hover:bg-primary/90"
                        >
                          Execute
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

      <SimulationResults
        open={showSimulation}
        onClose={() => setShowSimulation(false)}
        result={simulationResult}
        isLoading={simulatingPlanId !== null}
      />
    </div>
  )
}

