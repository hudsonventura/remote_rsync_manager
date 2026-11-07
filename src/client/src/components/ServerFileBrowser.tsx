import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { apiGet, apiPost } from "@/lib/api"
import { Folder, File, ArrowLeft, X, FolderPlus, RefreshCw } from "lucide-react"

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
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load directory")
    } finally {
      setIsLoading(false)
    }
  }

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

  const handleSelectCurrentPath = () => {
    const pathToSelect = currentPath || "/"
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
    } catch (err: any) {
      setCreateDirError(err instanceof Error ? err.message : (err?.message || "Failed to create directory"))
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
      <div className="relative z-50 w-full max-w-2xl max-h-[80vh] bg-background border rounded-lg shadow-lg flex flex-col">
        <div className="flex items-center justify-between p-4 border-b">
          <h2 className="text-xl font-semibold">Browse Server File System</h2>
          <Button variant="ghost" size="sm" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="flex items-center gap-2 p-4 border-b">
          <Button
            variant="outline"
            size="sm"
            onClick={handleGoBack}
            disabled={pathHistory.length <= 1 || isLoading}
          >
            <ArrowLeft className="h-4 w-4 mr-2" />
            Back
          </Button>
          <Input
            value={currentPath || "/"}
            readOnly
            className="flex-1 font-mono text-sm"
          />
          <Button
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
            variant="outline"
            size="sm"
            onClick={() => loadDirectory(currentPath)}
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
              Loading...
            </div>
          ) : items.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              This directory is empty
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

        <div className="flex items-center justify-end gap-2 p-4 border-t">
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={handleSelectCurrentPath}>
            Select Current Path
          </Button>
        </div>
      </div>
    </div>
  )
}
