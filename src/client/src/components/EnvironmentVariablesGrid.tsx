import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Plus, Trash2, Eye, EyeOff, KeyRound } from "lucide-react"

export type EnvVarType = "text" | "secret"

export interface EnvVar {
  key: string
  value: string
  type?: EnvVarType
  isSecret?: boolean
}

interface EnvironmentVariablesGridProps {
  variables: EnvVar[]
  onChange: (variables: EnvVar[]) => void
  disabled?: boolean
}

export function EnvironmentVariablesGrid({
  variables,
  onChange,
  disabled = false,
}: EnvironmentVariablesGridProps) {
  const [visibleValues, setVisibleValues] = useState<Record<number, boolean>>({})

  const handleAdd = () => {
    onChange([...variables, { key: "", value: "", type: "text", isSecret: false }])
  }

  const handleRemove = (index: number) => {
    const updated = variables.filter((_, i) => i !== index)
    onChange(updated)
  }

  const handleChangeKey = (index: number, key: string) => {
    const updated = [...variables]
    updated[index] = { ...updated[index], key }
    onChange(updated)
  }

  const handleChangeType = (index: number, type: EnvVarType) => {
    const updated = [...variables]
    updated[index] = {
      ...updated[index],
      type,
      isSecret: type === "secret",
    }
    onChange(updated)
  }

  const handleChangeValue = (index: number, value: string) => {
    const updated = [...variables]
    updated[index] = { ...updated[index], value }
    onChange(updated)
  }

  const toggleVisibility = (index: number) => {
    setVisibleValues((prev) => ({
      ...prev,
      [index]: !prev[index],
    }))
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <KeyRound className="h-4 w-4 text-muted-foreground" />
          <h4 className="text-sm font-semibold">Environment Variables</h4>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={handleAdd}
          disabled={disabled}
        >
          <Plus className="mr-1.5 h-3.5 w-3.5" />
          Add Variable
        </Button>
      </div>

      <p className="text-xs text-muted-foreground">
        Injected into the process environment during execution. In .NET, access them via{" "}
        <code className="bg-muted px-1 py-0.5 rounded font-mono text-[11px]">
          Environment.GetEnvironmentVariable("KEY")
        </code>
        .
      </p>

      <div className="rounded-md border bg-muted/40 p-3 text-xs space-y-1.5">
        <p className="font-semibold text-foreground flex items-center gap-1.5">
          <span>📌</span> Built-in Fixed Variables (Always available to extensions):
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-[11px] font-mono text-muted-foreground pt-1">
          <div className="p-1.5 rounded bg-background border">
            <span className="font-semibold text-foreground">STORE_PATH</span>: Storage outside container (<code className="text-primary">/store</code>)
          </div>
          <div className="p-1.5 rounded bg-background border">
            <span className="font-semibold text-foreground">HOST_PATH</span>: Target filesystem (<code className="text-primary">/host</code>)
          </div>
          <div className="p-1.5 rounded bg-background border">
            <span className="font-semibold text-foreground">EXTENSION_DIR</span>: Unpacked extension directory
          </div>
          <div className="p-1.5 rounded bg-background border">
            <span className="font-semibold text-foreground">DATA_PATH</span>: App data directory (<code className="text-primary">/app/data</code>)
          </div>
        </div>
      </div>

      {variables.length === 0 ? (
        <div className="rounded-lg border border-dashed p-4 text-center">
          <p className="text-xs text-muted-foreground">
            No environment variables configured. Click &quot;Add Variable&quot; to configure database strings, API keys, or custom tokens.
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          <div className="grid grid-cols-12 gap-2 text-xs font-semibold text-muted-foreground uppercase px-1">
            <div className="col-span-4 sm:col-span-4">Name (Key)</div>
            <div className="col-span-3 sm:col-span-2">Type</div>
            <div className="col-span-4 sm:col-span-5">Value</div>
            <div className="col-span-1 text-center">Action</div>
          </div>

          {variables.map((v, index) => {
            const isSecret = (v.type ?? (v.isSecret ? "secret" : "text")) === "secret"
            const isVisible = visibleValues[index] ?? false

            return (
              <div key={index} className="grid grid-cols-12 gap-2 items-center">
                <div className="col-span-4 sm:col-span-4">
                  <Input
                    placeholder="e.g. DATABASE_URL"
                    value={v.key}
                    onChange={(e) => handleChangeKey(index, e.target.value)}
                    disabled={disabled}
                    className="font-mono text-xs uppercase"
                  />
                </div>

                <div className="col-span-3 sm:col-span-2">
                  <select
                    value={isSecret ? "secret" : "text"}
                    onChange={(e) => handleChangeType(index, e.target.value as EnvVarType)}
                    disabled={disabled}
                    aria-label={`Tipo da variável ${v.key || index + 1}`}
                    className="flex h-9 w-full rounded-md border border-input bg-card px-2 py-1 text-xs shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 cursor-pointer"
                  >
                    <option value="text">Texto</option>
                    <option value="secret">Secret</option>
                  </select>
                </div>

                <div className="col-span-4 sm:col-span-5">
                  {isSecret ? (
                    <div className="relative">
                      <Input
                        type={isVisible ? "text" : "password"}
                        placeholder="Secret value..."
                        value={v.value}
                        onChange={(e) => handleChangeValue(index, e.target.value)}
                        disabled={disabled}
                        className="font-mono text-xs pr-9"
                      />
                      <button
                        type="button"
                        onClick={() => toggleVisibility(index)}
                        disabled={disabled}
                        className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                        title={isVisible ? "Hide secret" : "Show secret"}
                      >
                        {isVisible ? (
                          <EyeOff className="h-3.5 w-3.5" />
                        ) : (
                          <Eye className="h-3.5 w-3.5" />
                        )}
                      </button>
                    </div>
                  ) : (
                    <Input
                      type="text"
                      placeholder="Value..."
                      value={v.value}
                      onChange={(e) => handleChangeValue(index, e.target.value)}
                      disabled={disabled}
                      className="font-mono text-xs"
                    />
                  )}
                </div>

                <div className="col-span-1 flex justify-center">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-destructive hover:text-destructive hover:bg-destructive/10"
                    onClick={() => handleRemove(index)}
                    disabled={disabled}
                    title="Remove variable"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

