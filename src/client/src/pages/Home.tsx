export function Home() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Welcome</h1>
        <p className="text-muted-foreground mt-2">
          Welcome to your dashboard. This is your home page.
        </p>
      </div>
      <div className="rounded-lg border bg-card p-6 shadow-sm">
        <h2 className="text-xl font-semibold mb-4">Getting Started</h2>
        <p className="text-muted-foreground">
          Use the sidebar menu to navigate to different sections of the application.
        </p>
      </div>
    </div>
  )
}
