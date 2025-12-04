import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Trash2, Save, AlertCircle, Send } from "lucide-react"
import { apiGet, apiPost } from "@/lib/api"
import { Switch } from "@/components/ui/switch"

interface LogRetentionDateResponse {
  date: string | null
}

interface LogRetentionPeriodResponse {
  months: number | null
}

interface DeleteLogsResponse {
  executionsDeleted: number
  logsDeleted: number
  notificationsDeleted: number
  spaceSavedBytes: number
  message: string
}

interface TelegramConfigResponse {
  isEnabled: boolean
  botToken: string
  webhookUrl: string
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B"
  
  const k = 1024
  const sizes = ["B", "KB", "MB", "GB", "TB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

export function Settings() {
  const navigate = useNavigate()
  const [logRetentionDate, setLogRetentionDate] = useState<string>("")
  const [logRetentionMonths, setLogRetentionMonths] = useState<string>("")
  const [isSavingPeriod, setIsSavingPeriod] = useState(false)
  const [isDeleting, setIsDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [deleteResult, setDeleteResult] = useState<DeleteLogsResponse | null>(null)

  const [telegramEnabled, setTelegramEnabled] = useState(false)
  const [telegramBotToken, setTelegramBotToken] = useState("")
  const [telegramWebhookUrl, setTelegramWebhookUrl] = useState("")
  const [telegramNotificationsEnabled, setTelegramNotificationsEnabled] = useState(false)
  const [telegramNotificationChatId, setTelegramNotificationChatId] = useState("")
  const [telegramChatId, setTelegramChatId] = useState("")
  const [isSavingTelegram, setIsSavingTelegram] = useState(false)
  const [isTestingTelegram, setIsTestingTelegram] = useState(false)
  const [telegramError, setTelegramError] = useState<string | null>(null)
  const [telegramSuccess, setTelegramSuccess] = useState<string | null>(null)

  useEffect(() => {
    const fetchSettings = async () => {
      try {
        const token = sessionStorage.getItem("token")
        if (!token) {
          navigate("/login")
          return
        }

        const [dateResponse, periodResponse, telegramResponse] = await Promise.all([
          apiGet<LogRetentionDateResponse>("/api/settings/log-retention-date"),
          apiGet<LogRetentionPeriodResponse>("/api/settings/log-retention-period"),
          apiGet<TelegramConfigResponse>("/api/telegram/config")
        ])

        if (dateResponse.date) {
          // Convert date string to YYYY-MM-DD format for input
          const date = new Date(dateResponse.date)
          setLogRetentionDate(date.toISOString().split('T')[0])
        }

        if (periodResponse.months !== null && periodResponse.months !== undefined) {
          setLogRetentionMonths(periodResponse.months.toString())
        }

        setTelegramEnabled(telegramResponse.isEnabled)
        setTelegramBotToken(telegramResponse.botToken)
        setTelegramWebhookUrl(telegramResponse.webhookUrl)
        setTelegramNotificationsEnabled(telegramResponse.notificationsEnabled)
        setTelegramNotificationChatId(telegramResponse.notificationChatId)
      } catch (err) {
        console.error("Error fetching settings:", err)
      }
    }

    fetchSettings()
  }, [navigate])

  const handleSavePeriod = async () => {
    setIsSavingPeriod(true)
    setError(null)
    setSuccess(null)

    try {
      const months = logRetentionMonths ? parseInt(logRetentionMonths) : null
      if (months !== null && months < 0) {
        setError("Retention period must be 0 or greater")
        setIsSavingPeriod(false)
        return
      }

      await apiPost("/api/settings/log-retention-period", {
        months: months
      })

      setSuccess("Log retention period saved successfully")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save log retention period")
    } finally {
      setIsSavingPeriod(false)
    }
  }

  const handleDeleteLogs = async () => {
    if (!logRetentionDate) {
      setError("Please set a log retention date first")
      return
    }

    if (!confirm(`Are you sure you want to delete all logs before ${logRetentionDate}? This action cannot be undone.`)) {
      return
    }

    setIsDeleting(true)
    setError(null)
    setSuccess(null)
    setDeleteResult(null)

    try {
      const response: DeleteLogsResponse = await apiPost<DeleteLogsResponse>(
        "/api/settings/delete-logs-before-date",
        {
          beforeDate: logRetentionDate
        }
      )

      setDeleteResult(response)
      setSuccess(response.message)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to delete logs")
    } finally {
      setIsDeleting(false)
    }
  }

  const handleSaveTelegram = async () => {
    setIsSavingTelegram(true)
    setTelegramError(null)
    setTelegramSuccess(null)

    try {
      await apiPost("/api/telegram/config", {
        botToken: telegramBotToken,
        webhookUrl: telegramWebhookUrl,
        isEnabled: telegramEnabled,
        notificationsEnabled: telegramNotificationsEnabled,
        notificationChatId: telegramNotificationChatId
      })

      setTelegramSuccess("Telegram configuration saved successfully")
    } catch (err) {
      setTelegramError(err instanceof Error ? err.message : "Failed to save Telegram configuration")
    } finally {
      setIsSavingTelegram(false)
    }
  }

  const handleTestTelegram = async () => {
    if (!telegramChatId) {
      setTelegramError("Please enter your Chat ID")
      return
    }

    setIsTestingTelegram(true)
    setTelegramError(null)
    setTelegramSuccess(null)

    try {
      await apiPost("/api/telegram/test", {
        chatId: parseInt(telegramChatId)
      })

      setTelegramSuccess("Test message sent successfully! Check your Telegram.")
    } catch (err) {
      setTelegramError(err instanceof Error ? err.message : "Failed to send test message")
    } finally {
      setIsTestingTelegram(false)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Settings</h1>
        <p className="text-muted-foreground mt-2">
          Configure your application preferences
        </p>
      </div>

      {/* Log Retention Settings */}
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <div className="space-y-6">
          <div>
            <h3 className="text-lg font-semibold mb-4">Log Retention</h3>
            <p className="text-sm text-muted-foreground mb-4">
              Configure when to delete old backup logs. Logs older than the specified date will be deleted when you click the delete button.
            </p>
            
            <div className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="log-retention-date">Delete logs before date</Label>
                <Input
                  id="log-retention-date"
                  type="date"
                  value={logRetentionDate}
                  onChange={(e) => setLogRetentionDate(e.target.value)}
                  className="max-w-xs"
                />
                <p className="text-xs text-muted-foreground">
                  Select a date. All logs and executions before this date will be deleted.
                </p>
              </div>

              <div className="flex items-center gap-4">
                {/* <Button
                  onClick={handleSave}
                  disabled={isLoading}
                  variant="default"
                >
                  <Save className="h-4 w-4 mr-2" />
                  Save Date
                </Button> */}

                <Button
                  onClick={handleDeleteLogs}
                  disabled={isDeleting || !logRetentionDate}
                  variant="destructive"
                >
                  <Trash2 className="h-4 w-4 mr-2" />
                  {isDeleting ? "Deleting..." : "Delete Logs Before Date"}
                </Button>
              </div>

              <div className="border-t pt-4 mt-4">
                <div className="space-y-2">
                  <Label htmlFor="log-retention-months">Automatic deletion period (months)</Label>
                  <Input
                    id="log-retention-months"
                    type="number"
                    min="0"
                    value={logRetentionMonths}
                    onChange={(e) => setLogRetentionMonths(e.target.value)}
                    placeholder="e.g., 3 (for 3 months)"
                    className="max-w-xs"
                  />
                  <p className="text-xs text-muted-foreground">
                    Set the number of months to keep logs. Logs older than this period will be automatically deleted every hour. Set to 0 or leave empty to disable automatic deletion.
                  </p>
                </div>

                <div className="flex items-center gap-4 mt-4">
                  <Button
                    onClick={handleSavePeriod}
                    disabled={isSavingPeriod}
                    variant="default"
                  >
                    <Save className="h-4 w-4 mr-2" />
                    {isSavingPeriod ? "Saving..." : "Save Period"}
                  </Button>
                </div>
              </div>

              {error && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive flex items-center gap-2">
                  <AlertCircle className="h-4 w-4" />
                  {error}
                </div>
              )}

              {success && (
                <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
                  {success}
                </div>
              )}

              {deleteResult && (
                <div className="rounded-lg border bg-muted p-4 space-y-2">
                  <p className="text-sm font-medium">Deletion Summary:</p>
                  <ul className="text-sm text-muted-foreground space-y-1 list-disc list-inside">
                    <li>Executions deleted: {deleteResult.executionsDeleted}</li>
                    <li>Log entries deleted: {deleteResult.logsDeleted}</li>
                    <li>Notifications deleted: {deleteResult.notificationsDeleted}</li>
                    <li className="font-medium text-foreground">
                      Disk space saved: {formatBytes(deleteResult.spaceSavedBytes)}
                    </li>
                  </ul>
                  <p className="text-xs text-muted-foreground mt-2">
                    Database has been optimized to free disk space.
                  </p>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Legacy Preferences Section (keeping for now) */}
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <div className="space-y-6">
          <div>
            <h3 className="text-lg font-semibold mb-4">Preferences</h3>
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <label className="text-sm font-medium">Dark Mode</label>
                  <p className="text-sm text-muted-foreground">
                    Toggle dark mode theme
                  </p>
                </div>
                <input type="checkbox" className="h-4 w-4" />
              </div>
              <div className="flex items-center justify-between">
                <div>
                  <label className="text-sm font-medium">Notifications</label>
                  <p className="text-sm text-muted-foreground">
                    Enable email notifications
                  </p>
                </div>
                <input type="checkbox" className="h-4 w-4" defaultChecked />
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Telegram Integration Settings */}
      <div className="rounded-lg border bg-card p-6 shadow-sm shadow-corporate">
        <div className="space-y-6">
          <div>
            <h3 className="text-lg font-semibold mb-2">Telegram Integration</h3>
            <p className="text-sm text-muted-foreground mb-4">
              Configure Telegram bot for notifications and alerts
            </p>

            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <label className="text-sm font-medium">Enable Telegram Bot</label>
                  <p className="text-sm text-muted-foreground">
                    Activate Telegram notifications
                  </p>
                </div>
                <Switch
                  checked={telegramEnabled}
                  onCheckedChange={setTelegramEnabled}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="telegram-bot-token">Bot Token</Label>
                <Input
                  id="telegram-bot-token"
                  type="password"
                  value={telegramBotToken}
                  onChange={(e) => setTelegramBotToken(e.target.value)}
                  placeholder="Enter your Telegram bot token"
                  disabled={!telegramEnabled}
                />
                <p className="text-xs text-muted-foreground">
                  Get your bot token from <a href="https://t.me/botfather" target="_blank" rel="noopener noreferrer" className="text-primary hover:underline">@BotFather</a> on Telegram
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="telegram-webhook-url">Webhook URL (Optional)</Label>
                <Input
                  id="telegram-webhook-url"
                  type="text"
                  value={telegramWebhookUrl}
                  onChange={(e) => setTelegramWebhookUrl(e.target.value)}
                  placeholder="https://yourdomain.com/api/telegram/webhook"
                  disabled={!telegramEnabled}
                />
                <p className="text-xs text-muted-foreground">
                  Leave empty for polling mode. Set for production webhook mode.
                </p>
              </div>

              <div className="flex items-center gap-4">
                <Button
                  onClick={handleSaveTelegram}
                  disabled={isSavingTelegram || !telegramEnabled}
                  variant="default"
                >
                  <Save className="h-4 w-4 mr-2" />
                  {isSavingTelegram ? "Saving..." : "Save Configuration"}
                </Button>
              </div>

              <div className="border-t pt-4 mt-4">
                <h4 className="text-sm font-semibold mb-3">Backup Notifications</h4>
                <p className="text-xs text-muted-foreground mb-4">
                  Receive Telegram notifications when backup plans start, complete, or fail.
                </p>

                <div className="space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <label className="text-sm font-medium">Enable Backup Notifications</label>
                      <p className="text-sm text-muted-foreground">
                        Get notified about backup plan executions
                      </p>
                    </div>
                    <Switch
                      checked={telegramNotificationsEnabled}
                      onCheckedChange={setTelegramNotificationsEnabled}
                      disabled={!telegramEnabled}
                    />
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="telegram-notification-chat-id">Notification Chat ID</Label>
                    <Input
                      id="telegram-notification-chat-id"
                      type="text"
                      value={telegramNotificationChatId}
                      onChange={(e) => setTelegramNotificationChatId(e.target.value)}
                      placeholder="Enter Chat ID for notifications"
                      disabled={!telegramEnabled || !telegramNotificationsEnabled}
                    />
                    <p className="text-xs text-muted-foreground">
                      Chat ID where backup notifications will be sent. Use /chatid command with your bot to get this.
                    </p>
                  </div>
                </div>
              </div>

              <div className="border-t pt-4 mt-4">
                <h4 className="text-sm font-semibold mb-2">Test Connection</h4>
                <p className="text-xs text-muted-foreground mb-3">
                  Send a test message to verify your bot is working. Start a chat with your bot and use /chatid to get your Chat ID.
                </p>
                <div className="space-y-2">
                  <Label htmlFor="telegram-chat-id">Your Chat ID</Label>
                  <Input
                    id="telegram-chat-id"
                    type="text"
                    value={telegramChatId}
                    onChange={(e) => setTelegramChatId(e.target.value)}
                    placeholder="Enter your Telegram Chat ID"
                    disabled={!telegramEnabled}
                  />
                </div>
                <div className="flex items-center gap-4 mt-3">
                  <Button
                    onClick={handleTestTelegram}
                    disabled={isTestingTelegram || !telegramEnabled || !telegramChatId}
                    variant="secondary"
                  >
                    <Send className="h-4 w-4 mr-2" />
                    {isTestingTelegram ? "Sending..." : "Send Test Message"}
                  </Button>
                </div>
              </div>

              {telegramError && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive flex items-center gap-2">
                  <AlertCircle className="h-4 w-4" />
                  {telegramError}
                </div>
              )}

              {telegramSuccess && (
                <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
                  {telegramSuccess}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
