import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { CronDescription } from "@/components/CronDescription"
import { EnvironmentVariablesGrid, type EnvVar } from "@/components/EnvironmentVariablesGrid"
import { apiGet, apiPutForm } from "@/lib/api"
import { ArrowLeft, Loader2, Upload, AlertCircle } from "lucide-react"

export function EditExtension() {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()

  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [command, setCommand] = useState("")
  const [schedule, setSchedule] = useState("")
  const [active, setActive] = useState(true)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [envVars, setEnvVars] = useState<EnvVar[]>([])

  const [isFetching, setIsFetching] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchExtension = async () => {
      try {
        const ext = await apiGet(`/api/extension/${id}`)
        setName(ext.name)
        setDescription(ext.description || "")
        setCommand(ext.command)
        setSchedule(ext.schedule)
        setActive(ext.active)

        if (ext.environmentVariables) {
          try {
            const parsed = JSON.parse(ext.environmentVariables)
            if (Array.isArray(parsed)) {
              setEnvVars(
                parsed.map((item: any) => ({
                  key: item.key || item.Key || "",
                  value: item.value || item.Value || "",
                  isSecret: false,
                }))
              )
            } else if (typeof parsed === "object" && parsed !== null) {
              setEnvVars(
                Object.entries(parsed).map(([key, value]) => ({
                  key,
                  value: String(value),
                  isSecret: false,
                }))
              )
            }
          } catch {
            setEnvVars([])
          }
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load extension details")
      } finally {
        setIsFetching(false)
      }
    }

    if (id) {
      fetchExtension()
    }
  }, [id])

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
      setError("Please specify the command to run")
      return
    }

    if (!schedule.trim()) {
      setError("Please specify a valid cron schedule")
      return
    }

    setIsSaving(true)
    setError(null)

    try {
      const formData = new FormData()
      formData.append("name", name.trim())
      formData.append("description", description.trim())
      formData.append("command", command.trim())
      formData.append("schedule", schedule.trim())
      formData.append("active", active.toString())

      const validEnvVars = envVars
        .map((v) => ({ key: v.key.trim(), value: v.value }))
        .filter((v) => v.key.length > 0)
      formData.append("environmentVariables", JSON.stringify(validEnvVars))

      if (selectedFile) {
        formData.append("zipFile", selectedFile)
      }

      await apiPutForm(`/api/extension/${id}`, formData)
      navigate("/extensions")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update extension")
    } finally {
      setIsSaving(false)
    }
  }

  if (isFetching) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    )
  }

  return (
    <div className="max-w-2xl mx-auto space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="outline" size="icon" onClick={() => navigate("/extensions")}>
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Edit Extension</h1>
          <p className="text-muted-foreground">
            Update extension configuration or replace its binary package.
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
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            disabled={isSaving}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="description">Description (optional)</Label>
          <Input
            id="description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={isSaving}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="command">Command to Execute *</Label>
          <Input
            id="command"
            value={command}
            onChange={(e) => setCommand(e.target.value)}
            required
            disabled={isSaving}
            className="font-mono text-sm"
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="schedule">Schedule (Cron Expression) *</Label>
          <Input
            id="schedule"
            value={schedule}
            onChange={(e) => setSchedule(e.target.value)}
            required
            disabled={isSaving}
            className="font-mono text-sm"
          />
          <CronDescription cronExpression={schedule} />
        </div>

        <div className="space-y-2">
          <Label htmlFor="zipFile">Replace Application ZIP Package (optional)</Label>
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
                  disabled={isSaving}
                />
              </label>
              <span className="text-muted-foreground"> or drag and drop</span>
            </div>
            <p className="text-xs text-muted-foreground mt-1">
              Leave empty to keep existing application files, or select a new .zip to overwrite them.
            </p>
            {selectedFile && (
              <div className="mt-4 inline-flex items-center gap-2 rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
                📦 New Package: {selectedFile.name} ({(selectedFile.size / 1024 / 1024).toFixed(2)} MB)
              </div>
            )}
          </div>
        </div>

        <div className="border-t pt-4">
          <EnvironmentVariablesGrid
            variables={envVars}
            onChange={setEnvVars}
            disabled={isSaving}
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
            disabled={isSaving}
          />
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t">
          <Button
            type="button"
            variant="outline"
            onClick={() => navigate("/extensions")}
            disabled={isSaving}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={isSaving}>
            {isSaving ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Saving Changes...
              </>
            ) : (
              "Save Changes"
            )}
          </Button>
        </div>
      </form>
    </div>
  )
}

