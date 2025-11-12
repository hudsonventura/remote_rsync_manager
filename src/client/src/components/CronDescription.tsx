import { useMemo } from "react"
import { toString } from "cronstrue"
import CronExpressionParser from "cron-parser"

interface CronDescriptionProps {
  cronExpression: string
}

export function CronDescription({ cronExpression }: CronDescriptionProps) {
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
      
      // Calculate next execution time
      let nextExec: Date | null = null
      if (isValidCron) {
        try {
          // cron-parser uses parse as a static method on CronExpressionParser
          const interval = CronExpressionParser.parse(cronForParsing, {
            tz: Intl.DateTimeFormat().resolvedOptions().timeZone
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
    } catch (error) {
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
      relativeTime = `on ${date.toLocaleDateString()} at ${date.toLocaleTimeString()}`
    }
    
    return relativeTime
  }

  return (
    <div className={`rounded-md p-2 text-sm space-y-1 ${
      isValid 
        ? "bg-muted/50 text-foreground" 
        : "bg-destructive/15 text-destructive"
    }`}>
      <div>
        <span className="font-medium">Schedule: </span>
        <span>{description}</span>
      </div>
      {isValid && nextExecution && (
        <div className="text-xs text-muted-foreground">
          <span className="font-medium">Next execution: </span>
          <span>{formatNextExecution(nextExecution)}</span>
          <span className="ml-1">
            ({nextExecution.toLocaleString()})
          </span>
        </div>
      )}
    </div>
  )
}

