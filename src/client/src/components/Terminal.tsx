import { useEffect, useRef, useState } from "react"
import { Terminal as XTerm } from "@xterm/xterm"
import { FitAddon } from "@xterm/addon-fit"
import { HubConnection, HubConnectionBuilder } from "@microsoft/signalr"
import "@xterm/xterm/css/xterm.css"

interface TerminalProps {
  className?: string
}

export function Terminal({ className }: TerminalProps) {
  const terminalRef = useRef<HTMLDivElement>(null)
  const xtermRef = useRef<XTerm | null>(null)
  const fitAddonRef = useRef<FitAddon | null>(null)
  const connectionRef = useRef<HubConnection | null>(null)
  const [isConnected, setIsConnected] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!terminalRef.current) return

    // Initialize xterm
    const terminal = new XTerm({
      cursorBlink: true,
      fontSize: 14,
      fontFamily: 'Consolas, "Courier New", monospace',
      theme: {
        background: "#1e1e1e",
        foreground: "#d4d4d4",
        cursor: "#aeafad",
      },
    })

    const fitAddon = new FitAddon()
    terminal.loadAddon(fitAddon)

    terminal.open(terminalRef.current)
    fitAddon.fit()

    xtermRef.current = terminal
    fitAddonRef.current = fitAddon

    // Handle terminal input
    terminal.onData((data) => {
      if (connectionRef.current?.state === "Connected") {
        // Send ALL input to server, including passwords
        // xterm.js handles local echo automatically
        // When programs like sudo disable echo, xterm.js won't display characters
        // but we still need to send them to the process
        connectionRef.current.invoke("SendInput", data).catch((err) => {
          console.error("Error sending input:", err)
        })
      }
    })

    // Connect to SignalR
    const token = sessionStorage.getItem("token")
    if (!token) {
      setError("Not authenticated. Please log in.")
      return
    }

    // Construct the hub URL
    // If VITE_API_URL is set, use it; otherwise use current origin
    const apiUrl = import.meta.env.VITE_API_URL || ""
    let hubUrl: string
    
    if (apiUrl) {
      // Remove trailing slash if present
      hubUrl = `${apiUrl.replace(/\/$/, "")}/hubs/terminal`
    } else {
      // Use current origin (same origin as the page)
      const protocol = window.location.protocol === "https:" ? "https:" : "http:"
      const host = window.location.host
      hubUrl = `${protocol}//${host}/hubs/terminal`
    }
    
    console.log("Connecting to SignalR hub at:", hubUrl)

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => {
          // Return the token - SignalR will add it to query string for WebSocket
          return token
        },
        skipNegotiation: false, // Use negotiation (required for authentication)
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff: 0s, 2s, 10s, 30s, then 30s intervals
          if (retryContext.previousRetryCount === 0) return 0
          if (retryContext.previousRetryCount === 1) return 2000
          if (retryContext.previousRetryCount === 2) return 10000
          return 30000
        },
      })
      .build()

    connectionRef.current = connection

    // Handle terminal output
    connection.on("TerminalOutput", (output: string) => {
      // Write output to terminal - this includes password prompts
      // When programs like sudo disable echo, they won't send characters back
      // but xterm.js will still accept input and send it to the server
      terminal.write(output)
    })

    // Handle errors
    connection.on("TerminalError", (errorMessage: string) => {
      setError(errorMessage)
      terminal.writeln(`\r\n\x1b[31mError: ${errorMessage}\x1b[0m`)
    })

    // Handle connection events
    connection.onclose(() => {
      setIsConnected(false)
      terminal.writeln("\r\n\x1b[33mConnection closed. Reconnecting...\x1b[0m")
    })

    connection.onreconnecting(() => {
      setIsConnected(false)
      terminal.writeln("\r\n\x1b[33mReconnecting...\x1b[0m")
    })

    connection.onreconnected(() => {
      setIsConnected(true)
      terminal.writeln("\r\n\x1b[32mReconnected. Starting terminal...\x1b[0m")
      connection.invoke("StartTerminal").catch((err) => {
        console.error("Error starting terminal:", err)
      })
    })

    // Start connection
    connection
      .start()
      .then(() => {
        setIsConnected(true)
        setError(null)
        terminal.writeln("\x1b[32mConnected to terminal. Starting bash...\x1b[0m\r\n")
        return connection.invoke("StartTerminal")
      })
      .catch((err) => {
        console.error("Error starting connection:", err)
        let errorMessage = "Failed to connect to terminal"
        
        if (err.message) {
          errorMessage = err.message
        } else if (err instanceof Error) {
          errorMessage = err.toString()
        }
        
        // Provide more helpful error messages
        if (errorMessage.includes("Failed to fetch") || errorMessage.includes("network")) {
          errorMessage = "Network error: Could not reach the server. Please check:\n" +
            "1. The server is running\n" +
            "2. You have accepted the SSL certificate\n" +
            "3. The URL is correct"
        } else if (errorMessage.includes("401") || errorMessage.includes("Unauthorized")) {
          errorMessage = "Authentication failed. Please log in again."
        }
        
        setError(errorMessage)
        terminal.writeln(`\r\n\x1b[31mError: ${errorMessage}\x1b[0m`)
      })

    // Handle window resize
    const handleResize = () => {
      if (fitAddonRef.current && xtermRef.current) {
        fitAddonRef.current.fit()
        if (connectionRef.current?.state === "Connected" && xtermRef.current) {
          const cols = xtermRef.current.cols || 80
          const rows = xtermRef.current.rows || 24
          connectionRef.current.invoke("ResizeTerminal", cols, rows).catch((err) => {
            console.error("Error resizing terminal:", err)
          })
        }
      }
    }

    window.addEventListener("resize", handleResize)

    // Cleanup
    return () => {
      window.removeEventListener("resize", handleResize)
      connection.stop().catch((err) => {
        console.error("Error stopping connection:", err)
      })
      terminal.dispose()
    }
  }, [])

  return (
    <div className={className}>
      {error && (
        <div className="mb-2 rounded-md bg-destructive/15 p-2 text-sm text-destructive">
          {error}
        </div>
      )}
      <div
        ref={terminalRef}
        className="w-full rounded-md border bg-[#1e1e1e] p-2"
        style={{ minHeight: "400px" }}
      />
      {isConnected && (
        <div className="mt-2 text-xs text-muted-foreground">
          Terminal connected. Type commands to interact with bash.
          <br />
          <span className="text-yellow-600 dark:text-yellow-400">
            Note: When entering passwords (e.g., for sudo), characters won't be displayed for security. Just type your password and press Enter.
          </span>
        </div>
      )}
    </div>
  )
}

