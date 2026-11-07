import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom"
import { Layout } from "@/components/Layout"
import { Login } from "@/pages/Login"
import { Dashboard } from "@/pages/Dashboard"
import { Profile } from "@/pages/Profile"
import { Settings } from "@/pages/Settings"
import { AgentsList } from "@/pages/AgentsList"
import { AddAgent } from "@/pages/AddAgent"
import { EditAgent } from "@/pages/EditAgent"
import { AgentBackupPlans } from "@/pages/AgentBackupPlans"
import { AddBackupPlan } from "@/pages/AddBackupPlan"
import { BackupPlansList } from "@/pages/BackupPlansList"
import { EditBackupPlan } from "@/pages/EditBackupPlan"
import { BackupLogs } from "@/pages/BackupLogs"
import { AllLogs } from "@/pages/AllLogs"
import { About } from "@/pages/About"
import { Users } from "@/pages/Users"
import { ExtensionsList } from "@/pages/ExtensionsList"
import { AddExtension } from "@/pages/AddExtension"
import { EditExtension } from "@/pages/EditExtension"
import { ExtensionLogs } from "@/pages/ExtensionLogs"

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route element={<Layout />}>
          <Route path="/" element={<Dashboard />} />
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/agents" element={<AgentsList />} />
          <Route path="/agents/add" element={<AddAgent />} />
          <Route path="/agents/:id/edit" element={<EditAgent />} />
          <Route path="/agents/:agentId/backup-plans" element={<AgentBackupPlans />} />
          <Route path="/agents/:agentId/backup-plans/add" element={<AddBackupPlan />} />
          <Route path="/agents/:agentId/backup-plans/:planId/edit" element={<EditBackupPlan />} />
          <Route path="/backup-plans" element={<BackupPlansList />} />
          <Route path="/backup-plans/:planId/edit" element={<EditBackupPlan />} />
          <Route path="/backup-plans/:planId/logs" element={<BackupLogs />} />
          <Route path="/backup-plans/:planId/logs/:executionId" element={<BackupLogs />} />
          <Route path="/extensions" element={<ExtensionsList />} />
          <Route path="/extensions/add" element={<AddExtension />} />
          <Route path="/extensions/:id/edit" element={<EditExtension />} />
          <Route path="/extensions/:id/logs" element={<ExtensionLogs />} />
          <Route path="/logs" element={<AllLogs />} />
          <Route path="/logs/:executionId" element={<AllLogs />} />
          <Route path="/profile" element={<Profile />} />
          <Route path="/users" element={<Users />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/about" element={<About />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
