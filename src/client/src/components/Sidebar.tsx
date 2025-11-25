import { Link, useLocation } from "react-router-dom"
import { cn } from "@/lib/utils"
import { Home, Settings, User, LayoutDashboard, Server, Database, Info, Users, FileText } from "lucide-react"
import { useEffect, useState } from "react"
import { apiGet } from "@/lib/api"

interface UserData {
  isAdmin: boolean
}

const navigation = [
  { name: "Home", href: "/", icon: Home },
  { name: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
  { name: "Agents", href: "/agents", icon: Server },
  { name: "Backup Plans", href: "/backup-plans", icon: Database },
  { name: "Logs", href: "/logs", icon: FileText },
  { name: "Profile", href: "/profile", icon: User },
  { name: "Settings", href: "/settings", icon: Settings },
  { name: "About", href: "/about", icon: Info },
]

const adminNavigation = [
  { name: "Users", href: "/users", icon: Users },
]

export function Sidebar() {
  const location = useLocation()
  const [isAdmin, setIsAdmin] = useState(false)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    const loadUserData = async () => {
      try {
        const userData = await apiGet<UserData>("/api/users/me")
        setIsAdmin(userData.isAdmin)
      } catch (err) {
        console.warn("Failed to load user data:", err)
      } finally {
        setIsLoading(false)
      }
    }
    loadUserData()
  }, [])

  if (isLoading) {
    return (
      <div className="flex h-full w-64 flex-col border-r bg-card">
        <div className="flex h-16 items-center border-b px-6 gap-2">
          <img 
            src="/icon.png" 
            alt="Remember" 
            className="h-14 w-16"
          />
          <h2 className="text-lg font-semibold">Remember Backup System</h2>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full w-64 flex-col border-r bg-card">
      <div className="flex h-16 items-center border-b px-6 gap-2">
        <img 
          src="/icon.png" 
          alt="Remember" 
          className="h-14 w-16"
        />
        <h2 className="text-lg font-semibold">Remember Backup System</h2>
      </div>
      <nav className="flex-1 space-y-1 p-4">
        {navigation.map((item) => {
          const isActive = location.pathname === item.href
          const Icon = item.icon
          return (
            <Link
              key={item.name}
              to={item.href}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                isActive
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
              )}
            >
              <Icon className="h-5 w-5" />
              {item.name}
            </Link>
          )
        })}
        {isAdmin && (
          <>
            <div className="my-2 border-t" />
            {adminNavigation.map((item) => {
              const isActive = location.pathname === item.href
              const Icon = item.icon
              return (
                <Link
                  key={item.name}
                  to={item.href}
                  className={cn(
                    "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                    isActive
                      ? "bg-primary text-primary-foreground"
                      : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                  )}
                >
                  <Icon className="h-5 w-5" />
                  {item.name}
                </Link>
              )
            })}
          </>
        )}
      </nav>
    </div>
  )
}

