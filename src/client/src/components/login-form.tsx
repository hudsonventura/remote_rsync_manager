"use client"

import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { apiPost, apiGet, API_URL } from "@/lib/api"

interface AuthResponse {
  token: string
  email: string
  expiresAt: string
}

export function LoginForm() {
  const navigate = useNavigate()
  const [username, setUsername] = useState("")
  const [password, setPassword] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | React.ReactNode | null>(null)

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setError(null)
    setIsLoading(true)

    try {
      const data: AuthResponse = await apiPost<AuthResponse>("/login", {
        username,
        password,
      })
      
      // Store token in sessionStorage
      sessionStorage.setItem("token", data.token)
      sessionStorage.setItem("email", data.email)
      sessionStorage.setItem("expiresAt", data.expiresAt)

      // Load user preferences
      try {
        const userData = await apiGet<{ timezone?: string; theme?: string }>("/api/users/me")
        // Always set timezone from database if it exists, otherwise keep sessionStorage value or default to UTC
        if (userData.timezone) {
          sessionStorage.setItem("selectedTimezone", userData.timezone)
        } else {
          // If no timezone in database, ensure sessionStorage has a default value
          const currentTimezone = sessionStorage.getItem("selectedTimezone")
          if (!currentTimezone) {
            sessionStorage.setItem("selectedTimezone", "UTC")
          }
        }
        if (userData.theme) {
          localStorage.setItem("remember-ui-theme", userData.theme)
        }
      } catch (err) {
        // If fetching preferences fails, continue with defaults
        console.warn("Failed to load user preferences:", err)
        // On error, ensure we have a default timezone
        const currentTimezone = sessionStorage.getItem("selectedTimezone")
        if (!currentTimezone) {
          sessionStorage.setItem("selectedTimezone", "UTC")
        }
      }

      // Redirect to dashboard
      navigate("/")
    } catch (err) {
      if (err instanceof TypeError && err.message === "Failed to fetch") {
        // Determine the backend URL for the certificate error message
        let backendUrl: string | null = null
        
        if (API_URL && API_URL !== "" && !API_URL.startsWith("/")) {
          // Use the configured API_URL if it's a full URL
          backendUrl = API_URL
        } else {
          // If API_URL is relative or empty, try to infer from the login endpoint
          // The login endpoint is "/login", so we need to construct the full backend URL
          // For same-origin requests, we can use window.location.origin
          // But if the backend is on a different port, we need to detect it
          const currentOrigin = window.location.origin
          
          // Check if we're in development (common dev ports)
          if (currentOrigin.includes(":5173") || currentOrigin.includes(":3000") || currentOrigin.includes(":8080")) {
            // In development, backend is typically on port 5001
            backendUrl = currentOrigin.replace(/:\d+$/, ":5001")
          } else {
            // In production, assume same origin or use environment variable
            // If VITE_API_URL is not set, we can't determine the backend URL
            // In this case, we'll show a generic message
            backendUrl = import.meta.env.VITE_API_URL || currentOrigin
          }
        }
        
        // Only show certificate error if we have an HTTPS URL
        if (backendUrl && backendUrl.startsWith("https://")) {
          setError(
            <>
              Unable to connect to the server due to an invalid SSL certificate. 
              Please visit{" "}
              <a 
                href={backendUrl} 
                target="_blank" 
                rel="noopener noreferrer"
                className="underline font-medium hover:text-destructive/80"
              >
                {backendUrl}
              </a>
              {" "}in your browser first to accept the certificate, then try logging in again.
            </>
          )
        } else {
          setError("Unable to connect to the server. Please make sure the backend is running.")
        }
      } else {
        setError(err instanceof Error ? err.message : "An error occurred")
      }
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col items-center gap-2 text-center">
        <img 
          src="/icon.png" 
          alt="Remember Backup System" 
          className="h-128 w-128 mb-2"
        />
        <h1 className="text-2xl font-bold">Login</h1>
        <p className="text-balance text-muted-foreground">
          Enter your username below to login to your account
        </p>
      </div>
      <form onSubmit={handleSubmit} className="flex flex-col gap-6">
        {error && (
          <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
            {typeof error === "string" ? error : error}
          </div>
        )}
        <div className="flex flex-col gap-2">
          <Label htmlFor="username">Username</Label>
          <Input
            id="username"
            type="text"
            placeholder="admin"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            required
            disabled={isLoading}
          />
        </div>
        <div className="flex flex-col gap-2">
          <div className="flex items-center">
            <Label htmlFor="password">Password</Label>
            <a
              href="#"
              className="ml-auto text-sm underline-offset-4 hover:underline"
            >
              Forgot your password?
            </a>
          </div>
          <Input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            disabled={isLoading}
          />
        </div>
        <Button type="submit" className="w-full" disabled={isLoading}>
          {isLoading ? "Logging in..." : "Login"}
        </Button>
      </form>
    </div>
  )
}
