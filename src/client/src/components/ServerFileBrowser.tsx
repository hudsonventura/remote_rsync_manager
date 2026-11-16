import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { apiGet, apiPost } from "@/lib/api"
import { Folder, File, ArrowLeft, X, FolderPlus, RefreshCw, Server, FolderUp, CornerDownLeft } from "lucide-react"

interface FileSystemItem {
  name: string
  pathName: string  // Full path including filename/directory name
  path?: string     // Directory path without filename (for files)
  type: "file" | "directory"
  size?: number | null
  lastModified: string
  permissions?: string | null
  md5?: string | null
}

interface ServerFileBrowserProps {
  open: boolean
  onClose: () => void
  onSelect: (path: string) => void
  initialPath?: string
}

export function ServerFileBrowser({ open, onClose, onSelect, initialPath }: ServerFileBrowserProps) {
  const [currentPath, setCurrentPath] = useState("")
  const [inputPath, setInputPath] = useState("")
  const [items, setItems] = useState<FileSystemItem[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pathHistory, setPathHistory] = useState<string[]>([])
  const [isCreatingDir, setIsCreatingDir] = useState(false)
  const [newDirName, setNewDirName] = useState("")
  const [isSubmittingDir, setIsSubmittingDir] = useState(false)
  const [createDirError, setCreateDirError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setIsCreatingDir(false)
      setNewDirName("")
      setCreateDirError(null)

      // Use initialPath if provided, otherwise start with root path
      let startPath = "/"
      if (initialPath) {
        const normalizedPath = initialPath.trim()
        if (normalizedPath) {
          // Remove trailing slashes to normalize, but keep the path as-is
          startPath = normalizedPath.replace(/[/\\]+$/, "") || "/"
        }
      }
      setCurrentPath(startPath)
      setInputPath(startPath)
      setPathHistory([startPath])
      loadDirectory(startPath)
    }
  }, [open, initialPath])

  const loadDirectory = async (path: string) => {
    setIsLoading(true)
    setError(null)

    try {
      const url = path 
        ? `/api/filesystem/browse?dir=${encodeURIComponent(path)}`
        : `/api/filesystem/browse`
      const data: FileSystemItem[] = await apiGet<FileSystemItem[]>(url)
      setItems(data)
      
      // Set the current path to what we requested, or resolve to the actual item path if starting from "/"
      let displayPath = path || "/"
      if ((!path || path === "/") && data.length > 0 && data[0].path) {
        displayPath = data[0].path
      }
      setCurrentPath(displayPath)
      setInputPath(displayPath)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load directory")
    } finally {
      setIsLoading(false)
    }
  }

  const getParentDirectory = (path: string): string => {
    const trimmed = path.trim()
    if (!trimmed || trimmed === "/" || trimmed === "\\") {
      return "/"
    }
    const normalized = trimmed.replace(/[/\\]+$/, "")
    if (!normalized) return "/"
    const lastSlash = normalized.lastIndexOf("/")
    const lastBackslash = normalized.lastIndexOf("\\")
    const lastSep = Math.max(lastSlash, lastBackslash)
    if (lastSep <= 0) return "/"
    return normalized.substring(0, lastSep) || "/"
  }

  const isAtRoot = !currentPath || currentPath.trim() === "/" || currentPath.trim() === "\\" || currentPath.trim().replace(/[/\\]+$/, "") === ""

  const handleItemClick = (item: FileSystemItem) => {
    if (item.type === "directory") {
      // For directories, use pathName (full path) to navigate into it
      const newPath = item.pathName
      setPathHistory([...pathHistory, newPath])
      loadDirectory(newPath)
    } else {
      // For files, use pathName (full path including filename)
      onSelect(item.pathName)
      onClose()
    }
  }

  const handleGoBack = () => {
    if (pathHistory.length > 1) {
      const newHistory = pathHistory.slice(0, -1)
      const previousPath = newHistory[newHistory.length - 1]
      setPathHistory(newHistory)
      loadDirectory(previousPath)
    }
  }

  const handleGoUp = () => {
    if (isAtRoot || isLoading) return
    const parent = getParentDirectory(currentPath)
    setPathHistory((prev) => [...prev, parent])
    loadDirectory(parent)
  }

  const handleNavigateToInput = () => {
    const target = inputPath.trim() || "/"
    if (target === currentPath) {
      loadDirectory(target)
      return
    }
    setPathHistory((prev) => [...prev, target])
    loadDirectory(target)
  }

  const handleSelectCurrentPath = () => {
    const pathToSelect = inputPath.trim() || currentPath || "/"
    onSelect(pathToSelect)
    onClose()
  }

  const handleCreateDirectory = async () => {
    const trimmed = newDirName.trim()
    if (!trimmed) {
      setCreateDirError("Directory name is required")
      return
    }

    if (trimmed.includes("/") || trimmed.includes("\\") || trimmed.includes("..")) {
      setCreateDirError("Directory name cannot contain slashes or traversal (..) characters")
      return
    }

    setIsSubmittingDir(true)
    setCreateDirError(null)

    try {
      await apiPost<{ message: string; pathName?: string }>("/api/filesystem/create-directory", {
        parentPath: currentPath,
        directoryName: trimmed,
      })

      // Reset create dir form
      setNewDirName("")
      setIsCreatingDir(false)

      // Reload directory list to show the new folder
      await loadDirectory(currentPath)
    } catch (err: unknown) {
      setCreateDirError(err instanceof Error ? err.message : String((err as { message?: string })?.message || "Failed to create directory"))
    } finally {
      setIsSubmittingDir(false)
    }
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div
        className="fixed inset-0 bg-black/80"
        onClick={onClose}
      />
      <div
        className="relative z-50 w-full max-w-2xl max-h-[80vh] bg-background border rounded-lg shadow-lg flex flex-col"
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            onClose()
          }
          if (e.key === "Backspace" && !(e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement)) {
            e.preventDefault()
          }
        }}
      >
        <div className="flex items-center justify-between p-4 border-b">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
              <Server className="h-5 w-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-semibold leading-none">Browse Server File System</h2>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20">
                  Server (Destination)
                </span>
              </div>
              <p className="text-xs text-muted-foreground mt-1">
                Browsing local server filesystem for backup storage • Destination
              </p>
            </div>
          </div>
          <Button variant="ghost" size="sm" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="flex items-center gap-2 p-4 border-b">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleGoBack}
            disabled={pathHistory.length <= 1 || isLoading}
            title="Go back in history"
          >
            <ArrowLeft className="h-4 w-4 mr-1.5" />
            Back
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleGoUp}
            disabled={isAtRoot || isLoading}
            title="Go to parent directory (Up)"
          >
            <FolderUp className="h-4 w-4 mr-1.5" />
            Up
          </Button>
          <div className="flex-1 flex items-center relative">
            <span className="absolute left-3 text-xs font-mono font-medium text-emerald-600 dark:text-emerald-400 select-none pointer-events-none">
              server:
            </span>
            <Input
              value={inputPath}
              onChange={(e) => setInputPath(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault()
                  handleNavigateToInput()
                }
              }}
              className="pl-16 pr-8 font-mono text-sm"
              placeholder="/path/to/directory"
              disabled={isLoading}
            />
            {inputPath.trim() !== currentPath.trim() && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleNavigateToInput}
                className="absolute right-1 h-7 w-7 p-0 text-muted-foreground hover:text-foreground"
                title="Navigate to path (Enter)"
                disabled={isLoading}
              >
                <CornerDownLeft className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              setIsCreatingDir((prev) => !prev)
              setCreateDirError(null)
            }}
            disabled={isLoading || isSubmittingDir}
            title="Create Directory"
          >
            <FolderPlus className="h-4 w-4 mr-2" />
            New Folder
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => loadDirectory(inputPath.trim() || currentPath)}
            disabled={isLoading}
            title="Refresh"
          >
            <RefreshCw className={`h-4 w-4 ${isLoading ? "animate-spin" : ""}`} />
          </Button>
        </div>

        {isCreatingDir && (
          <div className="p-3 bg-muted/40 border-b space-y-2">
            <div className="flex items-center gap-2">
              <FolderPlus className="h-4 w-4 text-primary shrink-0" />
              <Input
                placeholder="Directory name (e.g. backup_dest)"
                value={newDirName}
                onChange={(e) => {
                  setNewDirName(e.target.value)
                  setCreateDirError(null)
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    handleCreateDirectory()
                  } else if (e.key === "Escape") {
                    setIsCreatingDir(false)
                    setNewDirName("")
                    setCreateDirError(null)
                  }
                }}
                autoFocus
                disabled={isSubmittingDir}
                className="flex-1 h-9 text-sm"
              />
              <Button
                size="sm"
                className="h-9"
                onClick={handleCreateDirectory}
                disabled={isSubmittingDir || !newDirName.trim()}
              >
                {isSubmittingDir ? "Creating..." : "Create"}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                className="h-9"
                onClick={() => {
                  setIsCreatingDir(false)
                  setNewDirName("")
                  setCreateDirError(null)
                }}
                disabled={isSubmittingDir}
              >
                Cancel
              </Button>
            </div>
            {createDirError && (
              <p className="text-xs text-destructive pl-6">{createDirError}</p>
            )}
          </div>
        )}

        <div className="flex-1 overflow-auto p-4">
          {error && (
            <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive mb-4">
              {error}
            </div>
          )}

          {isLoading ? (
            <div className="text-center py-8 text-muted-foreground">
              Loading server directory...
            </div>
          ) : items.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              This directory is empty on the server
            </div>
          ) : (
            <div className="space-y-1">
              {items.map((item) => (
                <button
                  key={item.pathName}
                  onClick={() => handleItemClick(item)}
                  className="w-full flex items-center gap-3 p-2 rounded hover:bg-accent text-left transition-colors"
                >
                  {item.type === "directory" ? (
                    <Folder className="h-5 w-5 text-blue-500 flex-shrink-0" />
                  ) : (
                    <File className="h-5 w-5 text-muted-foreground flex-shrink-0" />
                  )}
                  <div className="flex-1 min-w-0">
                    <div className="font-medium truncate">{item.name}</div>
                    {item.type === "file" && item.size != null && (
                      <div className="text-xs text-muted-foreground">
                        {((item.size ?? 0) / 1024).toFixed(2)} KB
                      </div>
                    )}
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="flex items-center justify-between p-4 border-t">
          <span className="text-xs text-muted-foreground font-mono truncate max-w-[280px]">
            server:{currentPath || "/"}
          </span>
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button onClick={handleSelectCurrentPath}>
              Select Server Path
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
