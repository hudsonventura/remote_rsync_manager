import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { apiGet } from "@/lib/api"
import { Folder, File, ArrowLeft, X, HardDrive, RefreshCw, FolderUp, CornerDownLeft } from "lucide-react"

interface FileSystemItem {
  name: string
  pathName: string  // Full path including filename
  path: string      // Directory path without filename
  type: "file" | "directory"
  size?: number | null
  lastModified: string
  permissions?: string | null
  md5?: string | null
}

interface FileBrowserProps {
  agentId: string
  agentName?: string
  agentHostname?: string
  open: boolean
  onClose: () => void
  onSelect: (path: string) => void
  initialPath?: string
}

export function FileBrowser({ agentId, agentName, agentHostname, open, onClose, onSelect, initialPath }: FileBrowserProps) {
  const [currentPath, setCurrentPath] = useState("/")
  const [inputPath, setInputPath] = useState("/")
  const [items, setItems] = useState<FileSystemItem[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pathHistory, setPathHistory] = useState<string[]>(["/"])

  useEffect(() => {
    if (open) {
      // Use initialPath if provided, otherwise default to "/"
      // Use the path as-is - if it's a directory, it will work; if it's a file, the API will handle it
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
  }, [open, agentId, initialPath])

  const loadDirectory = async (path: string) => {
    setIsLoading(true)
    setError(null)

    try {
      const data: FileSystemItem[] = await apiGet<FileSystemItem[]>(
        `/api/agent/${agentId}/browse?dir=${encodeURIComponent(path)}`
      )
      setItems(data)
      setCurrentPath(path)
      setInputPath(path)
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
    const parentPath = getParentDirectory(currentPath)
    setPathHistory((prev) => [...prev, parentPath])
    loadDirectory(parentPath)
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

  const handleSelectCurrentPath = () => {
    // When selecting a directory as the source, append trailing slash so rsync copies directory contents
    const rawPath = inputPath.trim() || currentPath || "/"
    const pathToSelect = rawPath ? (rawPath.endsWith("/") ? rawPath : `${rawPath}/`) : "/"
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
            <div className="p-2 rounded-lg bg-blue-500/10 text-blue-600 dark:text-blue-400">
              <HardDrive className="h-5 w-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-semibold leading-none">Browse Agent File System</h2>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20">
                  {agentName ? `Agent: ${agentName}` : "Agent (Source)"}
                </span>
              </div>
              <p className="text-xs text-muted-foreground mt-1">
                {agentName
                  ? `Browsing remote agent filesystem on ${agentName}${agentHostname ? ` (${agentHostname})` : ""} • Source`
                  : "Browsing remote agent filesystem • Source"}
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
            <span className="absolute left-3 text-xs font-mono font-medium text-blue-600 dark:text-blue-400 select-none pointer-events-none">
              agent:
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
            onClick={() => loadDirectory(inputPath.trim() || currentPath)}
            disabled={isLoading}
            title="Refresh"
          >
            <RefreshCw className={`h-4 w-4 ${isLoading ? "animate-spin" : ""}`} />
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
              Loading agent directory...
            </div>
          ) : items.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              This directory is empty on the agent
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
            agent:{currentPath}
          </span>
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button onClick={handleSelectCurrentPath}>
              Select Agent Path
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}

