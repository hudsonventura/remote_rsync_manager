import { useEffect, useState } from "react"
import { useNavigate, Outlet } from "react-router-dom"
import { Sidebar } from "./Sidebar"
import { Button } from "@/components/ui/button"
import { ThemeToggle } from "./ThemeToggle"
import { Notifications } from "./Notifications"
import { TimezoneSelector } from "./TimezoneSelector"
import { apiGet } from "@/lib/api"
import { useTheme } from "next-themes"

export function Layout() {
  const navigate = useNavigate()
  const [email, setEmail] = useState<string | null>(null)
  const { setTheme } = useTheme()

  useEffect(() => {
    // Check if user is authenticated
    const token = sessionStorage.getItem("token")
    const userEmail = sessionStorage.getItem("email")

    if (!token) {
      // Redirect to login if not authenticated
      navigate("/login")
      return
    }

    setEmail(userEmail)

    // Load user preferences
    const loadPreferences = async () => {
      try {
        const userData = await apiGet<{ timezone?: string; theme?: string }>("/api/users/me")
        // Always set timezone from database if it exists, otherwise keep sessionStorage value or default to UTC
        if (userData.timezone) {
          sessionStorage.setItem("selectedTimezone", userData.timezone)
          // Trigger timezone change event
          window.dispatchEvent(new CustomEvent('timezoneChanged', { detail: userData.timezone }))
        } else {
          // If no timezone in database, ensure sessionStorage has a default value
          const currentTimezone = sessionStorage.getItem("selectedTimezone")
          if (!currentTimezone) {
            sessionStorage.setItem("selectedTimezone", "UTC")
            window.dispatchEvent(new CustomEvent('timezoneChanged', { detail: "UTC" }))
          }
        }
        if (userData.theme) {
          localStorage.setItem("remember-ui-theme", userData.theme)
          setTheme(userData.theme)
        }
      } catch (err) {
        console.warn("Failed to load user preferences:", err)
        // On error, ensure we have a default timezone
        const currentTimezone = sessionStorage.getItem("selectedTimezone")
        if (!currentTimezone) {
          sessionStorage.setItem("selectedTimezone", "UTC")
        }
      }
    }
    loadPreferences()
  }, [navigate, setTheme])

  const handleLogout = () => {
    sessionStorage.removeItem("token")
    sessionStorage.removeItem("email")
    sessionStorage.removeItem("expiresAt")
    navigate("/login")
  }

  if (!email) {
    return null // Will redirect to login
  }

  return (
    <div className="flex h-svh w-full">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <header className="flex h-16 items-center justify-between border-b bg-background px-6">
          <div className="flex items-center gap-4">
            <span className="text-sm text-muted-foreground">
              Logged in as <span className="font-medium text-foreground">{email}</span>
            </span>
          </div>
          <div className="flex items-center gap-2">
            <TimezoneSelector />
            <Notifications />
            <ThemeToggle />
            <Button variant="outline" onClick={handleLogout}>
              Logout
            </Button>
          </div>
        </header>
        <main className="flex-1 overflow-y-auto bg-background p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

