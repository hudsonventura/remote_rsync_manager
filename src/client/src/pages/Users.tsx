import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { apiGet, apiPost, apiPut, apiDelete } from "@/lib/api"
import { Loader2, Edit, Trash2, UserPlus } from "lucide-react"

interface User {
  id: string
  username: string
  email: string
  isAdmin: boolean
  isActive: boolean
  createdAt: string
  updatedAt?: string
  timezone?: string
  theme?: string
}

export function Users() {
  const [users, setUsers] = useState<User[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [currentUser, setCurrentUser] = useState<User | null>(null)

  // Create user dialog
  const [showCreateDialog, setShowCreateDialog] = useState(false)
  const [newUser, setNewUser] = useState({
    username: "",
    email: "",
    password: "",
    isAdmin: false
  })
  const [isCreating, setIsCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  // Edit user dialog
  const [editingUser, setEditingUser] = useState<User | null>(null)
  const [editForm, setEditForm] = useState({
    username: "",
    email: "",
    isAdmin: false,
    isActive: true
  })
  const [isEditing, setIsEditing] = useState(false)
  const [editError, setEditError] = useState<string | null>(null)

  // Change password dialog
  const [passwordUser, setPasswordUser] = useState<User | null>(null)
  const [newPassword, setNewPassword] = useState("")
  const [isChangingPassword, setIsChangingPassword] = useState(false)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [passwordSuccess, setPasswordSuccess] = useState(false)

  // Change username dialog
  const [usernameUser, setUsernameUser] = useState<User | null>(null)
  const [newUsername, setNewUsername] = useState("")
  const [isChangingUsername, setIsChangingUsername] = useState(false)
  const [usernameError, setUsernameError] = useState<string | null>(null)
  const [usernameSuccess, setUsernameSuccess] = useState(false)

  useEffect(() => {
    loadUsers()
    loadCurrentUser()
  }, [])

  const loadUsers = async () => {
    try {
      setIsLoading(true)
      setError(null)
      const data = await apiGet<User[]>("/api/users")
      setUsers(data)
    } catch (err: any) {
      setError(err?.message || "Failed to load users")
    } finally {
      setIsLoading(false)
    }
  }

  const loadCurrentUser = async () => {
    try {
      const data = await apiGet<User>("/api/users/me")
      setCurrentUser(data)
    } catch (err) {
      console.warn("Failed to load current user:", err)
    }
  }

  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault()
    setCreateError(null)

    if (!newUser.username || newUser.username.length < 3) {
      setCreateError("Username must be at least 3 characters long")
      return
    }

    if (!newUser.email) {
      setCreateError("Email is required")
      return
    }

    if (!newUser.password || newUser.password.length < 3) {
      setCreateError("Password must be at least 3 characters long")
      return
    }

    try {
      setIsCreating(true)
      await apiPost("/api/users", newUser)
      setShowCreateDialog(false)
      setNewUser({ username: "", email: "", password: "", isAdmin: false })
      loadUsers()
    } catch (err: any) {
      setCreateError(err?.message || "Failed to create user")
    } finally {
      setIsCreating(false)
    }
  }

  const handleEditUser = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!editingUser) return

    setEditError(null)

    try {
      setIsEditing(true)
      await apiPut(`/api/users/${editingUser.id}`, editForm)
      setEditingUser(null)
      loadUsers()
    } catch (err: any) {
      setEditError(err?.message || "Failed to update user")
    } finally {
      setIsEditing(false)
    }
  }

  const handleDeleteUser = async (userId: string) => {
    if (!confirm("Are you sure you want to delete this user?")) {
      return
    }

    try {
      await apiDelete(`/api/users/${userId}`)
      loadUsers()
    } catch (err: any) {
      alert(err?.message || "Failed to delete user")
    }
  }

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!passwordUser) return

    setPasswordError(null)
    setPasswordSuccess(false)

    if (!newPassword || newPassword.length < 3) {
      setPasswordError("Password must be at least 3 characters long")
      return
    }

    try {
      setIsChangingPassword(true)
      await apiPut(`/api/users/${passwordUser.id}/change-password`, {
        newPassword
      })
      setPasswordSuccess(true)
      setNewPassword("")
      setTimeout(() => {
        setPasswordUser(null)
        setPasswordSuccess(false)
      }, 2000)
    } catch (err: any) {
      setPasswordError(err?.message || "Failed to change password")
    } finally {
      setIsChangingPassword(false)
    }
  }

  const handleChangeUsername = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!usernameUser) return

    setUsernameError(null)
    setUsernameSuccess(false)

    if (!newUsername || newUsername.length < 3) {
      setUsernameError("Username must be at least 3 characters long")
      return
    }

    try {
      setIsChangingUsername(true)
      await apiPut(`/api/users/${usernameUser.id}/change-username`, {
        newUsername
      })
      setUsernameSuccess(true)
      setNewUsername("")
      setTimeout(() => {
        setUsernameUser(null)
        setUsernameSuccess(false)
        loadUsers()
      }, 2000)
    } catch (err: any) {
      setUsernameError(err?.message || "Failed to change username")
    } finally {
      setIsChangingUsername(false)
    }
  }

  const openEditDialog = (user: User) => {
    setEditingUser(user)
    setEditForm({
      username: user.username,
      email: user.email,
      isAdmin: user.isAdmin,
      isActive: user.isActive
    })
    setEditError(null)
  }

  const openPasswordDialog = (user: User) => {
    setPasswordUser(user)
    setNewPassword("")
    setPasswordError(null)
    setPasswordSuccess(false)
  }

  const openUsernameDialog = (user: User) => {
    setUsernameUser(user)
    setNewUsername(user.username)
    setUsernameError(null)
    setUsernameSuccess(false)
  }

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">User Management</h1>
          <p className="text-muted-foreground mt-2">Loading...</p>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">User Management</h1>
        </div>
        <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
          {error}
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">User Management</h1>
          <p className="text-muted-foreground mt-2">
            Create, edit, and manage system users
          </p>
        </div>
        <Button onClick={() => setShowCreateDialog(true)}>
          <UserPlus className="h-4 w-4 mr-2" />
          Create User
        </Button>
      </div>

      {/* Users Table */}
      <div className="rounded-lg border bg-card shadow-sm overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-3 text-sm font-medium">Username</th>
                <th className="text-left p-3 text-sm font-medium">Email</th>
                <th className="text-left p-3 text-sm font-medium">Role</th>
                <th className="text-left p-3 text-sm font-medium">Status</th>
                <th className="text-left p-3 text-sm font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {users.map((user) => (
                <tr key={user.id} className="border-t hover:bg-muted/50">
                  <td className="p-3 text-sm">{user.username}</td>
                  <td className="p-3 text-sm text-muted-foreground">{user.email}</td>
                  <td className="p-3 text-sm">
                    {user.isAdmin ? (
                      <span className="px-2 py-1 rounded text-xs font-medium bg-blue-500/20 text-blue-600 dark:text-blue-400">
                        Admin
                      </span>
                    ) : (
                      <span className="px-2 py-1 rounded text-xs font-medium bg-gray-500/20 text-gray-600 dark:text-gray-400">
                        User
                      </span>
                    )}
                  </td>
                  <td className="p-3 text-sm">
                    {user.isActive ? (
                      <span className="text-green-600 dark:text-green-400">Active</span>
                    ) : (
                      <span className="text-red-600 dark:text-red-400">Inactive</span>
                    )}
                  </td>
                  <td className="p-3 text-sm">
                    <div className="flex items-center gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => openEditDialog(user)}
                      >
                        <Edit className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => openPasswordDialog(user)}
                        title="Change Password"
                      >
                        🔑
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => openUsernameDialog(user)}
                        title="Change Username"
                      >
                        👤
                      </Button>
                      {user.id !== currentUser?.id && (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => handleDeleteUser(user.id)}
                          className="text-destructive hover:text-destructive"
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Create User Dialog */}
      {showCreateDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-card rounded-lg border p-6 w-full max-w-md">
            <h2 className="text-xl font-semibold mb-4">Create New User</h2>
            <form onSubmit={handleCreateUser} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="create-username">Username</Label>
                <Input
                  id="create-username"
                  type="text"
                  value={newUser.username}
                  onChange={(e) => setNewUser({ ...newUser, username: e.target.value })}
                  required
                  minLength={3}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="create-email">Email</Label>
                <Input
                  id="create-email"
                  type="email"
                  value={newUser.email}
                  onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="create-password">Password</Label>
                <Input
                  id="create-password"
                  type="password"
                  value={newUser.password}
                  onChange={(e) => setNewUser({ ...newUser, password: e.target.value })}
                  required
                  minLength={3}
                />
              </div>
              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="create-admin"
                  checked={newUser.isAdmin}
                  onChange={(e) => setNewUser({ ...newUser, isAdmin: e.target.checked })}
                  className="rounded"
                />
                <Label htmlFor="create-admin">Administrator</Label>
              </div>
              {createError && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
                  {createError}
                </div>
              )}
              <div className="flex gap-2">
                <Button type="submit" disabled={isCreating}>
                  {isCreating ? (
                    <>
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                      Creating...
                    </>
                  ) : (
                    "Create User"
                  )}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setShowCreateDialog(false)
                    setCreateError(null)
                    setNewUser({ username: "", email: "", password: "", isAdmin: false })
                  }}
                >
                  Cancel
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit User Dialog */}
      {editingUser && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-card rounded-lg border p-6 w-full max-w-md">
            <h2 className="text-xl font-semibold mb-4">Edit User</h2>
            <form onSubmit={handleEditUser} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="edit-email">Email</Label>
                <Input
                  id="edit-email"
                  type="email"
                  value={editForm.email}
                  onChange={(e) => setEditForm({ ...editForm, email: e.target.value })}
                  required
                />
              </div>
              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="edit-admin"
                  checked={editForm.isAdmin}
                  onChange={(e) => setEditForm({ ...editForm, isAdmin: e.target.checked })}
                  className="rounded"
                />
                <Label htmlFor="edit-admin">Administrator</Label>
              </div>
              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="edit-active"
                  checked={editForm.isActive}
                  onChange={(e) => setEditForm({ ...editForm, isActive: e.target.checked })}
                  className="rounded"
                />
                <Label htmlFor="edit-active">Active</Label>
              </div>
              {editError && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
                  {editError}
                </div>
              )}
              <div className="flex gap-2">
                <Button type="submit" disabled={isEditing}>
                  {isEditing ? (
                    <>
                      <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                      Saving...
                    </>
                  ) : (
                    "Save Changes"
                  )}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setEditingUser(null)}
                >
                  Cancel
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Change Password Dialog */}
      {passwordUser && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-card rounded-lg border p-6 w-full max-w-md">
            <h2 className="text-xl font-semibold mb-4">
              Change Password for {passwordUser.username}
            </h2>
            <form onSubmit={handleChangePassword} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="admin-new-password">New Password</Label>
                <Input
                  id="admin-new-password"
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  minLength={3}
                />
              </div>
              {passwordError && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
                  {passwordError}
                </div>
              )}
              {passwordSuccess && (
                <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
                  Password changed successfully
                </div>
              )}
              <div className="flex gap-2">
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
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setPasswordUser(null)
                    setNewPassword("")
                    setPasswordError(null)
                    setPasswordSuccess(false)
                  }}
                >
                  Cancel
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Change Username Dialog */}
      {usernameUser && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-card rounded-lg border p-6 w-full max-w-md">
            <h2 className="text-xl font-semibold mb-4">
              Change Username for {usernameUser.username}
            </h2>
            <form onSubmit={handleChangeUsername} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="admin-new-username">New Username</Label>
                <Input
                  id="admin-new-username"
                  type="text"
                  value={newUsername}
                  onChange={(e) => setNewUsername(e.target.value)}
                  required
                  minLength={3}
                />
              </div>
              {usernameError && (
                <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
                  {usernameError}
                </div>
              )}
              {usernameSuccess && (
                <div className="rounded-md bg-green-500/15 p-3 text-sm text-green-600 dark:text-green-400">
                  Username changed successfully
                </div>
              )}
              <div className="flex gap-2">
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
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setUsernameUser(null)
                    setNewUsername("")
                    setUsernameError(null)
                    setUsernameSuccess(false)
                  }}
                >
                  Cancel
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}

