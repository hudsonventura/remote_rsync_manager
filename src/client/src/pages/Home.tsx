import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"

export function Home() {
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
    <div className="min-h-svh w-full bg-background">
      <div className="container mx-auto px-4 py-8">
        <header className="flex items-center justify-between mb-8">
          <h1 className="text-3xl font-bold">Welcome</h1>
          <button
            onClick={handleLogout}
            className="px-4 py-2 text-sm font-medium text-foreground hover:bg-accent rounded-md transition-colors"
          >
            Logout
          </button>
        </header>

        <main className="max-w-4xl mx-auto">
          <div className="bg-card border rounded-lg p-6 shadow-sm">
            <h2 className="text-2xl font-semibold mb-4">Home</h2>
            <p className="text-muted-foreground mb-4">
              You are logged in as <span className="font-medium text-foreground">{email}</span>
            </p>
            <p className="text-muted-foreground">
              Welcome to your dashboard. This is your home page.
            </p>
          </div>
        </main>
      </div>
    </div>
  )
}

