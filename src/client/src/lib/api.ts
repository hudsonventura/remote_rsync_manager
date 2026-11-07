/**
 * Centralized API configuration
 * Update the API_URL here to change the backend endpoint
 */

// Get API URL from environment variable, or use relative path for same-origin requests
// If VITE_API_URL is set, use it. Otherwise, use empty string for relative URLs (same origin)
// This allows the app to work in different deployment scenarios
export const API_URL = import.meta.env.VITE_API_URL || ""

console.log("API_URL", API_URL)
if (API_URL === "") {
  console.error("API_URL is not set")
}

/**
 * Get the authentication token from session storage
 */
function getAuthToken(): string | null {
  return sessionStorage.getItem("token")
}

/**
 * Get default headers for API requests
 */
function getDefaultHeaders(isFormData?: boolean): HeadersInit {
  const headers: Record<string, string> = {}

  if (!isFormData) {
    headers["Content-Type"] = "application/json"
  }

  const token = getAuthToken()
  if (token) {
    headers["Authorization"] = `Bearer ${token}`
  }

  return headers
}

/**
 * Make an authenticated API request
 */
export async function apiRequest<T = any>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  // If endpoint is already a full URL, use it as-is
  // If API_URL is set, prepend it to the endpoint
  // Otherwise, use the endpoint as a relative path (same origin)
  const url = endpoint.startsWith("http") 
    ? endpoint 
    : API_URL 
      ? `${API_URL}${endpoint}` 
      : endpoint
  
  const isFormData = typeof FormData !== "undefined" && options.body instanceof FormData
  const defaultHeaders = getDefaultHeaders(isFormData)

  const response = await fetch(url, {
    ...options,
    headers: {
      ...defaultHeaders,
      ...options.headers,
    },
  })

  if (!response.ok) {
    // Handle 401 Unauthorized - redirect to login
    if (response.status === 401) {
      sessionStorage.removeItem("token")
      sessionStorage.removeItem("email")
      sessionStorage.removeItem("expiresAt")
      window.location.href = "/login"
      throw new Error("Unauthorized - please log in again")
    }

    const errorData = await response.json().catch(() => ({ 
      message: `HTTP error! status: ${response.status}` 
    }))
    throw new Error(errorData.message || `Request failed with status ${response.status}`)
  }

  // Handle empty responses
  const contentType = response.headers.get("content-type")
  if (contentType && contentType.includes("application/json")) {
    return await response.json()
  }
  
  return await response.text() as T
}

/**
 * GET request helper
 */
export async function apiGet<T = any>(endpoint: string): Promise<T> {
  return apiRequest<T>(endpoint, { method: "GET" })
}

/**
 * POST request helper
 */
export async function apiPost<T = any>(endpoint: string, data?: any): Promise<T> {
  return apiRequest<T>(endpoint, {
    method: "POST",
    body: data ? JSON.stringify(data) : undefined,
  })
}

/**
 * PUT request helper
 */
export async function apiPut<T = any>(endpoint: string, data?: any): Promise<T> {
  return apiRequest<T>(endpoint, {
    method: "PUT",
    body: data ? JSON.stringify(data) : undefined,
  })
}

/**
 * DELETE request helper
 */
export async function apiDelete<T = any>(endpoint: string): Promise<T> {
  return apiRequest<T>(endpoint, { method: "DELETE" })
}

/**
 * POST FormData request helper (for file uploads)
 */
export async function apiPostForm<T = any>(endpoint: string, formData: FormData): Promise<T> {
  return apiRequest<T>(endpoint, {
    method: "POST",
    body: formData,
  })
}

/**
 * PUT FormData request helper (for file updates)
 */
export async function apiPutForm<T = any>(endpoint: string, formData: FormData): Promise<T> {
  return apiRequest<T>(endpoint, {
    method: "PUT",
    body: formData,
  })
}

