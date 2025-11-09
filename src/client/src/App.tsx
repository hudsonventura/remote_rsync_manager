import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom"
import { Home } from "@/pages/Home"
import { Login } from "@/pages/Login"

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<Home />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
