import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { apiPost } from "@/lib/api"

export function AddAgent() {
  const navigate = useNavigate()
  const [name, setName] = useState("New Agent")
  const [hostname, setHostname] = useState("")
  const [rsyncUser, setRsyncUser] = useState("")
  const [rsyncPort, setRsyncPort] = useState("22")
  const [rsyncSshKey, setRsyncSshKey] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setError(null)
    setSuccess(false)
    setIsLoading(true)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      await apiPost("/api/agent", {
        name: name.trim(),
        hostname: hostname.trim(),
        rsyncUser: rsyncUser.trim() || null,
        rsyncPort: rsyncPort ? parseInt(rsyncPort, 10) : null,
        rsyncSshKey: rsyncSshKey.trim() || null,
      })
      setSuccess(true)
      setName("New Agent")
      setHostname("")
      setRsyncUser("")
      setRsyncPort("22")
      setRsyncSshKey("")
      
      // Redirect to agents list after a short delay
      setTimeout(() => {
        navigate("/agents")
      }, 1500)
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

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Add Agent</h1>
        <p className="text-muted-foreground mt-2">
          Register a new rsync connection by providing hostname and SSH configuration
        </p>
      </div>

      <div className="rounded-lg border bg-card p-6 shadow-sm max-w-2xl">
        <form onSubmit={handleSubmit} className="space-y-6">
          {error && (
            <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
              {error}
            </div>
          )}

          {success && (
            <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
              Agent created successfully! Redirecting...
            </div>
          )}

          <div className="space-y-2">
            <Label htmlFor="name">Name *</Label>
            <Input
              id="name"
              type="text"
              placeholder="New Agent"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              disabled={isLoading}
              className="max-w-md"
            />
            <p className="text-sm text-muted-foreground">
              A friendly name to identify this agent
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="hostname">Hostname *</Label>
            <Input
              id="hostname"
              type="text"
              placeholder="localhost:5001 or https://agent.example.com"
              value={hostname}
              onChange={(e) => setHostname(e.target.value)}
              required
              disabled={isLoading}
              className="max-w-md"
            />
            <p className="text-sm text-muted-foreground">
              Enter the hostname or IP address of the remote server for rsync connections
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="rsyncUser">Rsync/SSH User</Label>
            <Input
              id="rsyncUser"
              type="text"
              placeholder="username"
              value={rsyncUser}
              onChange={(e) => setRsyncUser(e.target.value)}
              disabled={isLoading}
              className="max-w-md"
            />
            <p className="text-sm text-muted-foreground">
              SSH username for rsync connections (optional)
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="rsyncPort">Rsync/SSH Port</Label>
            <Input
              id="rsyncPort"
              type="number"
              placeholder="22"
              value={rsyncPort}
              onChange={(e) => setRsyncPort(e.target.value)}
              disabled={isLoading}
              className="max-w-md"
              min="1"
              max="65535"
            />
            <p className="text-sm text-muted-foreground">
              SSH port for rsync connections (default: 22)
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="rsyncSshKey">SSH Private Key</Label>
            <textarea
              id="rsyncSshKey"
              placeholder="-----BEGIN OPENSSH PRIVATE KEY-----&#10;...&#10;-----END OPENSSH PRIVATE KEY-----"
              value={rsyncSshKey}
              onChange={(e) => setRsyncSshKey(e.target.value)}
              disabled={isLoading}
              rows={6}
              className="flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 font-mono text-xs max-w-md"
            />
            <p className="text-sm text-muted-foreground">
              Paste your SSH private key content here (optional). The key will be stored securely and used for rsync authentication.
            </p>
          </div>

          <div className="flex gap-4">
            <Button type="submit" disabled={isLoading}>
              {isLoading ? "Creating..." : "Create Agent"}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate("/")}
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

