import { useEffect, useState } from "react"

export function Profile() {
  const [email, setEmail] = useState<string | null>(null)

  useEffect(() => {
    const userEmail = sessionStorage.getItem("email")
    setEmail(userEmail)
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Profile</h1>
        <p className="text-muted-foreground mt-2">
          Manage your account information
        </p>
      </div>
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <div className="space-y-4">
          <div>
            <label className="text-sm font-medium text-muted-foreground">Email</label>
            <p className="mt-1 text-lg font-medium">{email || "Not available"}</p>
          </div>
          <div>
            <label className="text-sm font-medium text-muted-foreground">Account Status</label>
            <p className="mt-1 text-lg font-medium text-green-600">Active</p>
          </div>
        </div>
      </div>
    </div>
  )
}

