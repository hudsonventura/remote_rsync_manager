import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { apiGet, apiPut } from "@/lib/api"
import { CheckCircle2, XCircle, Loader2 } from "lucide-react"

interface UserData {
  id: string
  username: string
  email: string
  isAdmin: boolean
  isActive: boolean
  timezone?: string
  theme?: string
}

interface ChangeUsernameResponse {
  id: string
  username: string
  email: string
  isAdmin: boolean
  isActive: boolean
  timezone?: string
  theme?: string
}

export function Profile() {
  const [userData, setUserData] = useState<UserData | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Change username form
  const [newUsername, setNewUsername] = useState("")
  const [isChangingUsername, setIsChangingUsername] = useState(false)
  const [usernameError, setUsernameError] = useState<string | null>(null)
  const [usernameSuccess, setUsernameSuccess] = useState(false)

  // Change password form
  const [newPassword, setNewPassword] = useState("")
  const [confirmPassword, setConfirmPassword] = useState("")
  const [isChangingPassword, setIsChangingPassword] = useState(false)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [passwordSuccess, setPasswordSuccess] = useState(false)

  useEffect(() => {
    const loadUserData = async () => {
      try {
        setIsLoading(true)
        const data = await apiGet<UserData>("/api/users/me")
        setUserData(data)
        setNewUsername(data.username)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load user data")
      } finally {
        setIsLoading(false)
      }
    }
    loadUserData()
  }, [])

  const handleChangeUsername = async (e: React.FormEvent) => {
    e.preventDefault()
    setUsernameError(null)
    setUsernameSuccess(false)

    // Only admins can change username
    if (!userData?.isAdmin) {
      setUsernameError("Only administrators can change usernames")
      return
    }

    if (!newUsername || newUsername.length < 3) {
      setUsernameError("Username must be at least 3 characters long")
      return
    }

    if (newUsername === userData?.username) {
      setUsernameError("New username must be different from current username")
      return
    }

    try {
      setIsChangingUsername(true)
      const updatedUser = await apiPut<ChangeUsernameResponse>("/api/users/me/change-username", {
        newUsername
      })
      setUserData(updatedUser)
      setUsernameSuccess(true)
      setTimeout(() => setUsernameSuccess(false), 3000)
    } catch (err: any) {
      setUsernameError(err?.message || "Failed to change username")
    } finally {
      setIsChangingUsername(false)
    }
  }

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault()
    setPasswordError(null)
    setPasswordSuccess(false)

    if (!newPassword || newPassword.length < 3) {
      setPasswordError("New password must be at least 3 characters long")
      return
    }

    if (newPassword !== confirmPassword) {
      setPasswordError("New passwords do not match")
      return
    }

    try {
      setIsChangingPassword(true)
      await apiPut("/api/users/me/change-password", {
        newPassword
      })
      setPasswordSuccess(true)
      setNewPassword("")
      setConfirmPassword("")
      setTimeout(() => setPasswordSuccess(false), 3000)
    } catch (err: any) {
      setPasswordError(err?.message || "Failed to change password")
    } finally {
      setIsChangingPassword(false)
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">Profile</h1>
          <p className="text-muted-foreground mt-2">Loading...</p>
        </div>
      </div>
    )
  }

  if (error || !userData) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">Profile</h1>
          <p className="text-muted-foreground mt-2">Error loading profile</p>
        </div>
        {error && (
          <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
            {error}
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Profile</h1>
        <p className="text-muted-foreground mt-2">
          Manage your account information
        </p>
      </div>

      {/* User Information */}
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <h2 className="text-lg font-semibold mb-4">Account Information</h2>
        <div className="space-y-4">
          <div>
            <label className="text-sm font-medium text-muted-foreground">Username</label>
            <p className="mt-1 text-lg font-medium">{userData.username}</p>
          </div>
          <div>
            <label className="text-sm font-medium text-muted-foreground">Email</label>
            <p className="mt-1 text-lg font-medium">{userData.email}</p>
          </div>
          <div>
            <label className="text-sm font-medium text-muted-foreground">Role</label>
            <p className="mt-1 text-lg font-medium">
              {userData.isAdmin ? "Administrator" : "User"}
            </p>
          </div>
          <div>
            <label className="text-sm font-medium text-muted-foreground">Account Status</label>
            <p className="mt-1 text-lg font-medium text-green-600">Active</p>
          </div>
        </div>
      </div>

      {/* Change Username - Only for Admins */}
      {userData.isAdmin && (
        <div className="rounded-lg border bg-card p-6 shadow-sm">
          <h2 className="text-lg font-semibold mb-4">Change Username</h2>
          <form onSubmit={handleChangeUsername} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="new-username">New Username</Label>
              <Input
                id="new-username"
                type="text"
                value={newUsername}
                onChange={(e) => setNewUsername(e.target.value)}
                placeholder="Enter new username"
                required
                minLength={3}
                disabled={isChangingUsername}
              />
            </div>
            {usernameError && (
              <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive flex items-center gap-2">
                <XCircle className="h-4 w-4" />
                {usernameError}
              </div>
            )}
            {usernameSuccess && (
              <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400 flex items-center gap-2">
                <CheckCircle2 className="h-4 w-4" />
                Username changed successfully
              </div>
            )}
            <Button type="submit" disabled={isChangingUsername}>
              {isChangingUsername ? (
                <>
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                  Changing...
                </>
              ) : (
                "Change Username"
              )}
            </Button>
          </form>
        </div>
      )}

      {/* Change Password */}
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <h2 className="text-lg font-semibold mb-4">Change Password</h2>
        <form onSubmit={handleChangePassword} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="new-password">New Password</Label>
            <Input
              id="new-password"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="Enter new password"
              required
              minLength={3}
              disabled={isChangingPassword}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirm-password">Confirm New Password</Label>
            <Input
              id="confirm-password"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="Confirm new password"
              required
              minLength={3}
              disabled={isChangingPassword}
            />
          </div>
          {passwordError && (
            <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive flex items-center gap-2">
              <XCircle className="h-4 w-4" />
              {passwordError}
            </div>
          )}
          {passwordSuccess && (
            <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400 flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4" />
              Password changed successfully
            </div>
          )}
          <Button type="submit" disabled={isChangingPassword}>
            {isChangingPassword ? (
              <>
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                Changing...
              </>
            ) : (
              "Change Password"
            )}
          </Button>
        </form>
      </div>
    </div>
  )
}

