import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { RefreshCw, Copy, Check, Terminal as TerminalIcon } from "lucide-react"
import { apiPost } from "@/lib/api"
import { Terminal } from "@/components/Terminal"

export function AddAgent() {
  const navigate = useNavigate()
  const [name, setName] = useState("New Agent")
  const [hostname, setHostname] = useState("")
  const [rsyncUser, setRsyncUser] = useState("")
  const [rsyncPort, setRsyncPort] = useState("22")
  const [rsyncSshKey, setRsyncSshKey] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [isValidating, setIsValidating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [validationMessage, setValidationMessage] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const [copied, setCopied] = useState(false)
  const [showTerminal, setShowTerminal] = useState(false)

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

  const handleValidate = async () => {
    if (!hostname.trim()) {
      setError("Hostname is required to validate the connection")
      setValidationMessage(null)
      return
    }

    if (!rsyncSshKey.trim()) {
      setError("SSH private key is required to validate the connection")
      setValidationMessage(null)
      return
    }

    setIsValidating(true)
    setValidationMessage(null)
    setError(null)

    try {
      const token = sessionStorage.getItem("token")
      if (!token) {
        navigate("/login")
        return
      }

      const result = await apiPost<{ 
        message: string; 
        hostname: string;
        hasSshKey?: boolean;
        rsyncUser?: string;
        rsyncPort?: number;
      }>(
        "/api/agent/validate",
        {
          hostname: hostname.trim(),
          rsyncUser: rsyncUser.trim() || null,
          rsyncPort: rsyncPort ? parseInt(rsyncPort, 10) : 22,
          rsyncSshKey: rsyncSshKey.trim(),
        }
      )
      
      setValidationMessage(`✓ ${result.message}`)
      setError(null)
    } catch (err: any) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        setError("Unable to connect to the server. Please make sure the backend is running.")
      } else {
        // The apiPost helper already extracts the error message from the response
        // so err.message should contain the message from the API
        let errorMessage = "An error occurred during validation"
        
        if (err?.message) {
          errorMessage = err.message
        } else if (typeof err === 'string') {
          errorMessage = err
        }
        
        // Format multi-line error messages for better display
        setError(errorMessage)
        setValidationMessage(null)
      }
    } finally {
      setIsValidating(false)
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
            <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive whitespace-pre-line">
              {error}
            </div>
          )}

          {success && (
            <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
              Agent created successfully! Redirecting...
            </div>
          )}

          {validationMessage && (
            <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
              {validationMessage}
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
              disabled={isLoading || isValidating}
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
              disabled={isLoading || isValidating}
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
              disabled={isLoading || isValidating}
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
              disabled={isLoading || isValidating}
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
              disabled={isLoading || isValidating}
              rows={6}
              className="flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 font-mono text-xs max-w-md"
            />
            <p className="text-sm text-muted-foreground">
              Paste your SSH private key content here (optional). The key will be stored securely and used for rsync authentication.
            </p>
          </div>

          {/* SSH Key Generation */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label>Generate SSH Key</Label>
            </div>
            
            {showTerminal ? (
              <div className="space-y-2">
                <Terminal className="w-full" />
                <p className="text-sm text-muted-foreground">
                  Use the terminal above to generate SSH keys. You can run commands like <code className="bg-muted px-1 rounded">ssh-keygen -t ed25519 -f ./id_ed25519 -N ""</code> and then copy the private key content to the SSH Private Key field above.
                </p>
              </div>
            ) : (
              <>
                <div className="relative">
                  <pre className="flex items-start justify-between gap-4 rounded-md border bg-muted p-4 text-sm font-mono overflow-x-auto">
                    <code className="flex-1 whitespace-pre">
{(() => {
  const userAtHost = rsyncUser && hostname.trim()
    ? `${rsyncUser.trim()}@${hostname.trim()}`
    : hostname.trim()
    ? hostname.trim()
    : "user@remote-ip"
  return `cd /tmp/ && ssh-keygen -t ed25519 -f ./${hostname.trim()} -N "" && \\
mkdir -p ~/.ssh && chmod 700 ~/.ssh && \\
ssh-copy-id -i ./${hostname.trim()}.pub ${userAtHost}&& \\
cat ./${hostname.trim()}`
})()}
                    </code>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={async () => {
                        const userAtHost = rsyncUser && hostname.trim()
                          ? `${rsyncUser.trim()}@${hostname.trim()}`
                          : hostname.trim()
                          ? hostname.trim()
                          : "user@remote-ip"
                        const commands = `cd /tmp/ && ssh-keygen -t ed25519 -f ./${hostname.trim()} -N "" && \\
mkdir -p ~/.ssh && chmod 700 ~/.ssh && \\
ssh-copy-id -i ./${hostname.trim()}.pub ${userAtHost}&& \\
cat ./${hostname.trim()}`
                        try {
                          await navigator.clipboard.writeText(commands)
                          setCopied(true)
                          setTimeout(() => setCopied(false), 2000)
                        } catch (err) {
                          console.error("Failed to copy commands:", err)
                        }
                      }}
                      className="shrink-0"
                    >
                      {copied ? (
                        <>
                          <Check className="h-4 w-4 mr-2" />
                          Copied
                        </>
                      ) : (
                        <>
                          <Copy className="h-4 w-4 mr-2" />
                          Copy
                        </>
                      )}
                    </Button>
                  </pre>
                </div>
                <p className="text-sm text-muted-foreground">
                  Use these commands to generate an SSH key pair and copy the public key to the remote server. Copy the command and past into a terminal to generate the keys.
                </p>
              </>
            )}
          </div>

          <div className="flex gap-4">
            <Button type="submit" disabled={isLoading || isValidating}>
              {isLoading ? "Creating..." : "Create Agent"}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={handleValidate}
              disabled={isLoading || isValidating}
            >
              <RefreshCw className={`h-4 w-4 mr-2 ${isValidating ? "animate-spin" : ""}`} />
              {isValidating ? "Validating..." : "Validate Connection"}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate("/")}
              disabled={isLoading || isValidating}
            >
              Cancel
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

