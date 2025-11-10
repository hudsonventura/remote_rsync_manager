import { useEffect, useState } from "react"
import { useNavigate, Outlet } from "react-router-dom"
import { Sidebar } from "./Sidebar"
import { Button } from "@/components/ui/button"
import { ThemeToggle } from "./ThemeToggle"

export function Layout() {
  const navigate = useNavigate()
  const [email, setEmail] = useState<string | null>(null)

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
  }, [navigate])

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

