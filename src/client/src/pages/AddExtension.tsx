import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { CronDescription } from "@/components/CronDescription"
import { EnvironmentVariablesGrid, type EnvVar } from "@/components/EnvironmentVariablesGrid"
import { apiPostForm } from "@/lib/api"
import { ArrowLeft, Loader2, Upload, AlertCircle } from "lucide-react"

export function AddExtension() {
  const navigate = useNavigate()
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [command, setCommand] = useState("dotnet App.dll")
  const [schedule, setSchedule] = useState("0 * * * *")
  const [active, setActive] = useState(true)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [envVars, setEnvVars] = useState<EnvVar[]>([])

  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0]
      if (!file.name.toLowerCase().endsWith(".zip")) {
        setError("Please select a valid .zip archive file")
        setSelectedFile(null)
        return
      }
      setSelectedFile(file)
      setError(null)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!name.trim()) {
      setError("Please provide an extension name")
      return
    }

    if (!command.trim()) {
      setError("Please specify the command to run (e.g., dotnet App.dll)")
      return
    }

    if (!schedule.trim()) {
      setError("Please specify a valid cron schedule")
      return
    }

    if (!selectedFile) {
      setError("Please upload a .zip file containing the compiled application")
      return
    }

    setIsLoading(true)
    setError(null)

    try {
      const formData = new FormData()
      formData.append("name", name.trim())
      formData.append("description", description.trim())
      formData.append("command", command.trim())
      formData.append("schedule", schedule.trim())
      formData.append("active", active.toString())
      formData.append("zipFile", selectedFile)

      const validEnvVars = envVars
        .map((v) => ({ key: v.key.trim(), value: v.value }))
        .filter((v) => v.key.length > 0)
      if (validEnvVars.length > 0) {
        formData.append("environmentVariables", JSON.stringify(validEnvVars))
      }

      await apiPostForm("/api/extension", formData)
      navigate("/extensions")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create and install extension")
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="max-w-2xl mx-auto space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="outline" size="icon" onClick={() => navigate("/extensions")}>
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Add Extension</h1>
          <p className="text-muted-foreground">
            Install an external .NET application packaged as a ZIP file.
          </p>
        </div>
      </div>

      {error && (
        <div className="rounded-lg bg-destructive/15 border border-destructive/20 p-4 text-destructive flex items-center gap-3">
          <AlertCircle className="h-5 w-5 shrink-0" />
          <p className="text-sm font-medium">{error}</p>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-6 rounded-lg border bg-card p-6 shadow-sm">
        <div className="space-y-2">
          <Label htmlFor="name">Extension Name *</Label>
          <Input
            id="name"
            placeholder="e.g., ZFS Snapshot Notifier"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            disabled={isLoading}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="description">Description (optional)</Label>
          <Input
            id="description"
            placeholder="Brief description of what this extension does"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={isLoading}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="command">Command to Execute *</Label>
          <Input
            id="command"
            placeholder="e.g., dotnet App.dll or dotnet Worker.dll --verbose"
            value={command}
            onChange={(e) => setCommand(e.target.value)}
            required
            disabled={isLoading}
            className="font-mono text-sm"
          />
          <p className="text-xs text-muted-foreground">
            Provide the entry point command. The working directory will be set to the unpacked extension root.
          </p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="schedule">Schedule (Cron Expression) *</Label>
          <Input
            id="schedule"
            placeholder="0 * * * *"
            value={schedule}
            onChange={(e) => setSchedule(e.target.value)}
            required
            disabled={isLoading}
            className="font-mono text-sm"
          />
          <CronDescription cronExpression={schedule} />
        </div>

        <div className="space-y-2">
          <Label htmlFor="zipFile">Application ZIP Package *</Label>
          <div className="rounded-lg border border-dashed p-6 text-center hover:bg-accent/50 transition-colors">
            <Upload className="mx-auto h-8 w-8 text-muted-foreground mb-2" />
            <div className="text-sm">
              <label
                htmlFor="zipFile"
                className="cursor-pointer font-medium text-primary hover:underline"
              >
                <span>Click to browse</span>
                <input
                  id="zipFile"
                  type="file"
                  accept=".zip"
                  onChange={handleFileChange}
                  className="sr-only"
                  disabled={isLoading}
                />
              </label>
              <span className="text-muted-foreground"> or drag and drop</span>
            </div>
            <p className="text-xs text-muted-foreground mt-1">
              Upload a .zip file containing your compiled .NET binaries and dependencies.
            </p>
            {selectedFile && (
              <div className="mt-4 inline-flex items-center gap-2 rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
                📦 {selectedFile.name} ({(selectedFile.size / 1024 / 1024).toFixed(2)} MB)
              </div>
            )}
          </div>
        </div>

        <div className="border-t pt-4">
          <EnvironmentVariablesGrid
            variables={envVars}
            onChange={setEnvVars}
            disabled={isLoading}
          />
        </div>

        <div className="flex items-center justify-between rounded-lg border p-4">
          <div className="space-y-0.5">
            <Label htmlFor="active" className="text-base font-medium">
              Enable Scheduled Execution
            </Label>
            <p className="text-xs text-muted-foreground">
              When enabled, the extension will run automatically according to its cron schedule.
            </p>
          </div>
          <Switch
            id="active"
            checked={active}
            onCheckedChange={setActive}
            disabled={isLoading}
          />
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t">
          <Button
            type="button"
            variant="outline"
            onClick={() => navigate("/extensions")}
            disabled={isLoading}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Uploading & Installing...
              </>
            ) : (
              "Install Extension"
            )}
          </Button>
        </div>
      </form>
    </div>
  )
}

