import { useEffect, useMemo, useState } from "react"
import { toString } from "cronstrue"
import CronExpressionParser from "cron-parser"

interface CronDescriptionProps {
  cronExpression: string
  timezone?: string
}

export function CronDescription({ cronExpression, timezone: propTimezone }: CronDescriptionProps) {
  const [selectedTimezone, setSelectedTimezone] = useState<string>(() => {
    if (propTimezone) return propTimezone
    return sessionStorage.getItem("selectedTimezone") || "UTC"
  })

  useEffect(() => {
    if (propTimezone) {
      setSelectedTimezone(propTimezone)
      return
    }

    const handleTimezoneChange = (event: CustomEvent<string>) => {
      const newTimezone = event.detail || "UTC"
      setSelectedTimezone(newTimezone)
    }

    window.addEventListener("timezoneChanged", handleTimezoneChange as EventListener)

    const saved = sessionStorage.getItem("selectedTimezone")
    if (saved && saved !== selectedTimezone) {
      setSelectedTimezone(saved)
    }

    return () => {
      window.removeEventListener("timezoneChanged", handleTimezoneChange as EventListener)
    }
  }, [propTimezone, selectedTimezone])

  const { description, nextExecution, isValid } = useMemo(() => {
    if (!cronExpression || cronExpression.trim() === "") {
      return { description: null, nextExecution: null, isValid: true }
    }

    try {
      // cronstrue expects a 5-field cron expression (minute hour day month weekday)
      const parts = cronExpression.trim().split(/\s+/)
      let cronForParsing = cronExpression.trim()
      
      // If it's a 6-field cron (with seconds), remove the seconds field for cron-parser
      if (parts.length === 6) {
        cronForParsing = parts.slice(1).join(" ")
      }
      
      // Validate it's a 5-field cron
      if (parts.length !== 5 && parts.length !== 6) {
        return { 
          description: "Invalid cron expression format (expected 5 fields)", 
          nextExecution: null,
          isValid: false 
        }
      }

      // Get human-readable description
      const result = toString(cronForParsing, {
        throwExceptionOnParseError: false,
        verbose: true,
      })
      
      const isValidCron = result !== "Invalid cron expression"
      
      // Calculate next execution time in UTC
      let nextExec: Date | null = null
      if (isValidCron) {
        try {
          // cron-parser uses parse as a static method on CronExpressionParser
          // Use UTC timezone to match server timezone
          const interval = CronExpressionParser.parse(cronForParsing, {
            tz: 'UTC'
          })
          nextExec = interval.next().toDate()
        } catch (parseError) {
          // If parsing fails, just don't show next execution
          console.warn("Failed to parse cron for next execution:", parseError)
        }
      }

      return { 
        description: result, 
        nextExecution: nextExec,
        isValid: isValidCron 
      }
    } catch {
      return { 
        description: "Invalid cron expression", 
        nextExecution: null,
        isValid: false 
      }
    }
  }, [cronExpression])

  if (!description) {
    return null
  }

  const formatNextExecution = (date: Date | null) => {
    if (!date) return null
    
    const now = new Date()
    const diffMs = date.getTime() - now.getTime()
    const diffMins = Math.floor(diffMs / 60000)
    const diffHours = Math.floor(diffMs / 3600000)
    const diffDays = Math.floor(diffMs / 86400000)
    
    let relativeTime: string
    if (diffMins < 1) {
      relativeTime = "in less than a minute"
    } else if (diffMins < 60) {
      relativeTime = `in ${diffMins} minute${diffMins !== 1 ? 's' : ''}`
    } else if (diffHours < 24) {
      relativeTime = `in ${diffHours} hour${diffHours !== 1 ? 's' : ''}`
    } else if (diffDays < 7) {
      relativeTime = `in ${diffDays} day${diffDays !== 1 ? 's' : ''}`
    } else {
      relativeTime = `in ${diffDays} days`
    }
    
    return relativeTime
  }
  
  const formatFullDateTime = (date: Date | null, tz: string = "UTC", includeOffset: boolean = false) => {
    if (!date) return null
    
    try {
      // Format date and time in 24-hour format
      const dateStr = date.toLocaleDateString('en-US', { 
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        timeZone: tz
      })
      const timeStr = date.toLocaleTimeString('en-US', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false,
        timeZone: tz
      })
      
      if (includeOffset && tz !== "UTC") {
        try {
          const offsetFormatter = new Intl.DateTimeFormat('en-US', {
            timeZone: tz,
            timeZoneName: 'short'
          })
          const parts = offsetFormatter.formatToParts(date)
          const tzName = parts.find(p => p.type === 'timeZoneName')?.value || ''
          if (tzName) {
            return `${dateStr} ${timeStr} (${tzName})`
          }
        } catch {
          // Ignore offset formatting error
        }
      }
      
      return `${dateStr} ${timeStr}`
    } catch {
      // Fallback to UTC if timezone is invalid
      const dateStr = date.toLocaleDateString('en-US', { 
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        timeZone: 'UTC'
      })
      const timeStr = date.toLocaleTimeString('en-US', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false,
        timeZone: 'UTC'
      })
      return `${dateStr} ${timeStr}`
    }
  }

  const hasConfiguredTimezone = Boolean(selectedTimezone && selectedTimezone !== "UTC")

  return (
    <div className={`rounded-md p-2 text-sm space-y-1.5 ${
      isValid 
        ? "bg-muted/50 text-foreground" 
        : "bg-destructive/15 text-destructive"
    }`}>
      <div>
        <span className="font-medium">Schedule (UTC): </span>
        <span>{description}</span>
      </div>
      {isValid && nextExecution && (
        <div className="text-xs text-muted-foreground space-y-1">
          <div>
            <span className="font-medium">Next execution: </span>
            <span>{formatNextExecution(nextExecution)}</span>
            {!hasConfiguredTimezone && (
              <span className="ml-1">
                ({formatFullDateTime(nextExecution, "UTC")} UTC)
              </span>
            )}
          </div>
          {hasConfiguredTimezone && (
            <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
              <span className="inline-flex items-center gap-1 rounded bg-background/80 border border-border/50 px-1.5 py-0.5">
                <span className="font-semibold text-foreground/90">UTC:</span>
                <span>{formatFullDateTime(nextExecution, "UTC")}</span>
              </span>
              <span className="inline-flex items-center gap-1 rounded bg-background/80 border border-border/50 px-1.5 py-0.5">
                <span className="font-semibold text-foreground/90" title={selectedTimezone}>
                  {selectedTimezone}:
                </span>
                <span>{formatFullDateTime(nextExecution, selectedTimezone, true)}</span>
              </span>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
