import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Plus, Pencil } from "lucide-react"
import { apiGet } from "@/lib/api"

interface Agent {
  id: string
  hostname: string
}

export function AgentsList() {
  const navigate = useNavigate()
  const [agents, setAgents] = useState<Agent[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchAgents = async () => {
    setIsLoading(true)
    setError(null)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      const data: Agent[] = await apiGet<Agent[]>("/api/agent")
      setAgents(data)
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        setError(err instanceof Error ? err.message : "An error occurred while fetching agents")
      }
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    fetchAgents()
  }, [navigate])

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold">Agents</h1>
            <p className="text-muted-foreground mt-2">
              Manage your backup agents
            </p>
          </div>
        </div>
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <p className="text-muted-foreground">Loading agents...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">Agents</h1>
          <p className="text-muted-foreground mt-2">
            Manage your backup agents
          </p>
        </div>
        <Button onClick={() => navigate("/agents/add")}>
          <Plus className="h-4 w-4 mr-2" />
          Add Agent
        </Button>
      </div>

      {error && (
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
          <Button
            variant="outline"
            size="sm"
            className="ml-4"
            onClick={fetchAgents}
          >
            Retry
          </Button>
        </div>
      )}

      {!error && agents.length === 0 && (
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <div className="text-center py-12">
            <p className="text-muted-foreground mb-4">No agents found</p>
            <Button onClick={() => navigate("/agents/add")}>
              <Plus className="h-4 w-4 mr-2" />
              Add Your First Agent
            </Button>
          </div>
        </div>
      )}

      {!error && agents.length > 0 && (
        <div className="rounded-lg border bg-card shadow-sm">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b">
                  <th className="h-12 px-4 text-left align-middle font-medium text-muted-foreground">
                    Hostname
                  </th>
                  <th className="h-12 px-4 text-left align-middle font-medium text-muted-foreground">
                    ID
                  </th>
                  <th className="h-12 px-4 text-left align-middle font-medium text-muted-foreground">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {agents.map((agent) => (
                  <tr
                    key={agent.id}
                    className="border-b transition-colors hover:bg-muted/50"
                  >
                    <td 
                      className="p-4 align-middle font-medium cursor-pointer"
                      onClick={() => navigate(`/agents/${agent.id}/backup-plans`)}
                    >
                      {agent.hostname}
                    </td>
                    <td 
                      className="p-4 align-middle text-sm text-muted-foreground font-mono cursor-pointer"
                      onClick={() => navigate(`/agents/${agent.id}/backup-plans`)}
                    >
                      {agent.id}
                    </td>
                    <td className="p-4 align-middle">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={(e) => {
                          e.stopPropagation()
                          navigate(`/agents/${agent.id}/edit`)
                        }}
                      >
                        <Pencil className="h-4 w-4 mr-2" />
                        Edit
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}

