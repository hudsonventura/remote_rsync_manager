import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { apiGet } from "@/lib/api"
import { Folder, File, ArrowLeft, X } from "lucide-react"

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

  useEffect(() => {
    if (open) {
      // Use initialPath if provided, otherwise start with root path
      // Determine default root path based on OS (but we'll use "/" for simplicity)
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
      
      // Set the current path to what we requested (or default to "/")
      const displayPath = path || "/"
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
            onClick={() => loadDirectory(currentPath)}
            disabled={isLoading}
          >
            Refresh
          </Button>
        </div>

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

