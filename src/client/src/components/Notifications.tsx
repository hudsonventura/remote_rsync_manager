import { useEffect, useState, useRef } from "react"
import { useNavigate } from "react-router-dom"
import { Bell, X, CheckCheck } from "lucide-react"
import { Button } from "@/components/ui/button"
import { apiGet, apiPost, apiDelete } from "@/lib/api"
import { formatDateTimeWithTimezone } from "@/components/TimezoneSelector"

interface Notification {
  id: string
  type: string
  title: string
  message: string
  backupPlanId: string | null
  executionId: string | null
  isRead: boolean
  createdAt: string
}

function formatDateTime(dateTime: string, timezone: string = "UTC"): string {
  return formatDateTimeWithTimezone(dateTime, timezone)
}

export function Notifications() {
  const navigate = useNavigate()
  const [notifications, setNotifications] = useState<Notification[]>([])
  const [unreadCount, setUnreadCount] = useState(0)
  const [isOpen, setIsOpen] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [notificationPermission, setNotificationPermission] = useState<NotificationPermission>("default")
  const [previousUnreadCount, setPreviousUnreadCount] = useState(0)
  const dropdownRef = useRef<HTMLDivElement>(null)
  const [timezone, setTimezone] = useState<string>("UTC")

  const fetchNotifications = async () => {
    try {
      const token = sessionStorage.getItem("token")
      if (!token) return

      const [notificationsData, countData] = await Promise.all([
        apiGet<Notification[]>("/api/notifications?unreadOnly=false&limit=20"),
        apiGet<{ count: number }>("/api/notifications/unread-count")
      ])

      setNotifications(notificationsData)
      setUnreadCount(countData.count)
    } catch (err) {
      console.error("Error fetching notifications:", err)
    }
  }

  // Listen for timezone changes from navbar
  useEffect(() => {
    const handleTimezoneChange = (event: CustomEvent) => {
      const newTimezone = event.detail || "UTC"
      setTimezone(newTimezone)
    }

    window.addEventListener('timezoneChanged', handleTimezoneChange as EventListener)

    // Load initial timezone from sessionStorage
    const saved = sessionStorage.getItem("selectedTimezone")
    if (saved) {
      setTimezone(saved)
    }

    return () => {
      window.removeEventListener('timezoneChanged', handleTimezoneChange as EventListener)
    }
  }, [])

  // Request notification permission on mount
  useEffect(() => {
    if ("Notification" in window) {
      const permission = Notification.permission
      setNotificationPermission(permission)

      if (permission === "default") {
        // Request permission when component mounts
        Notification.requestPermission().then((permission) => {
          setNotificationPermission(permission)
        })
      }
    }
  }, [])

  const handleNotificationClick = (notification: Notification) => {
    if (!notification.isRead) {
      handleMarkAsRead(notification.id)
    }

    if (notification.backupPlanId && notification.executionId) {
      navigate(`/backup-plans/${notification.backupPlanId}/logs/${notification.executionId}`)
      setIsOpen(false)
    } else if (notification.backupPlanId) {
      navigate(`/backup-plans/${notification.backupPlanId}/logs`)
      setIsOpen(false)
    }
  }

  // Show browser notifications when new unread notifications arrive
  useEffect(() => {
    if (notificationPermission === "granted" && unreadCount > previousUnreadCount && previousUnreadCount > 0) {
      // New notifications arrived
      const newNotifications = notifications.filter(n => !n.isRead)
      const latestNotification = newNotifications[0] // Most recent unread

      if (latestNotification) {
        const browserNotification = new Notification(latestNotification.title, {
          body: latestNotification.message,
          icon: "/icon.png",
          badge: "/icon.png",
          tag: latestNotification.id, // Prevent duplicate notifications
          requireInteraction: false,
        })

        browserNotification.onclick = () => {
          window.focus()
          handleNotificationClick(latestNotification)
          browserNotification.close()
        }

        // Auto-close after 5 seconds
        setTimeout(() => {
          browserNotification.close()
        }, 5000)
      }
    }

    setPreviousUnreadCount(unreadCount)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unreadCount, notifications, notificationPermission, previousUnreadCount])

  useEffect(() => {
    fetchNotifications()

    // Poll for new notifications every 5 seconds
    const interval = setInterval(fetchNotifications, 5000)

    return () => clearInterval(interval)
  }, [])

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    if (isOpen) {
      document.addEventListener("mousedown", handleClickOutside)
    }

    return () => {
      document.removeEventListener("mousedown", handleClickOutside)
    }
  }, [isOpen])

  const handleMarkAsRead = async (id: string) => {
    try {
      await apiPost(`/api/notifications/${id}/read`)
      await fetchNotifications()
    } catch (err) {
      console.error("Error marking notification as read:", err)
    }
  }

  const handleMarkAllAsRead = async () => {
    try {
      setIsLoading(true)
      await apiPost("/api/notifications/mark-all-read")
      await fetchNotifications()
    } catch (err) {
      console.error("Error marking all as read:", err)
    } finally {
      setIsLoading(false)
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await apiDelete(`/api/notifications/${id}`)
      await fetchNotifications()
    } catch (err) {
      console.error("Error deleting notification:", err)
    }
  }

  const getNotificationColor = (type: string) => {
    switch (type) {
      case "BackupCompleted":
        return "bg-green-500/20 text-green-600 dark:text-green-400"
      case "SimulationCompleted":
        return "bg-blue-500/20 text-blue-600 dark:text-blue-400"
      default:
        return "bg-muted text-muted-foreground"
    }
  }

  const handleEnableNotifications = async () => {
    if ("Notification" in window) {
      const permission = await Notification.requestPermission()
      setNotificationPermission(permission)
    }
  }

  return (
    <div className="relative" ref={dropdownRef}>
      <Button
        variant="outline"
        size="icon"
        onClick={() => setIsOpen(!isOpen)}
        className="relative"
        title={notificationPermission === "denied" ? "Browser notifications are blocked. Please enable them in your browser settings." : ""}
      >
        <Bell className="h-5 w-5" />
        {unreadCount > 0 && (
          <span className="absolute -top-1 -right-1 h-5 w-5 rounded-full bg-red-500 text-white text-xs flex items-center justify-center">
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
        {notificationPermission === "denied" && (
          <span className="absolute -bottom-1 -right-1 h-2 w-2 rounded-full bg-yellow-500"></span>
        )}
      </Button>

      {isOpen && (
        <div className="absolute right-0 mt-2 w-96 bg-card border rounded-lg shadow-lg z-50 max-h-[600px] overflow-hidden flex flex-col">
          <div className="flex items-center justify-between p-4 border-b">
            <h3 className="font-semibold">Notifications</h3>
            <div className="flex items-center gap-2">
              {notificationPermission !== "granted" && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleEnableNotifications}
                  title="Enable browser push notifications"
                >
                  Enable Push
                </Button>
              )}
              {unreadCount > 0 && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleMarkAllAsRead}
                  disabled={isLoading}
                >
                  <CheckCheck className="h-4 w-4 mr-1" />
                  Mark all read
                </Button>
              )}
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setIsOpen(false)}
              >
                <X className="h-4 w-4" />
              </Button>
            </div>
          </div>

          <div className="overflow-y-auto flex-1">
            {notifications.length === 0 ? (
              <div className="p-8 text-center text-muted-foreground">
                <Bell className="h-12 w-12 mx-auto mb-2 opacity-50" />
                <p>No notifications</p>
              </div>
            ) : (
              <div className="divide-y">
                {notifications.map((notification) => (
                  <div
                    key={notification.id}
                    className={`p-4 hover:bg-muted/50 cursor-pointer transition-colors ${
                      !notification.isRead ? "bg-blue-50/50 dark:bg-blue-950/20" : ""
                    }`}
                    onClick={() => handleNotificationClick(notification)}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <span className={`px-2 py-0.5 rounded text-xs font-medium ${getNotificationColor(notification.type)}`}>
                            {notification.type === "BackupCompleted" ? "Backup" : "Simulation"}
                          </span>
                          {!notification.isRead && (
                            <span className="h-2 w-2 rounded-full bg-blue-500"></span>
                          )}
                        </div>
                        <h4 className="font-medium text-sm mb-1">{notification.title}</h4>
                        <p className="text-sm text-muted-foreground line-clamp-2">
                          {notification.message}
                        </p>
                        <p className="text-xs text-muted-foreground mt-2">
                          {formatDateTime(notification.createdAt, timezone)}
                        </p>
                      </div>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-6 w-6 flex-shrink-0"
                        onClick={(e) => {
                          e.stopPropagation()
                          handleDelete(notification.id)
                        }}
                      >
                        <X className="h-3 w-3" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

