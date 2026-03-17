# AIHub WPF Application

AIHub is a .NET 8 WPF productivity workspace that connects to **Supabase** for auth, projects, logs, API keys, workflows, and analytics.  
This fork has been converted from a mostly UI-only scaffold into a functional application with real Supabase integration, proper navigation guards, and working CRUD for the core modules.

---

## Project Structure

Top-level layout:

- `AIHub.csproj` – WPF application project targeting `net8.0-windows`.
- `App.xaml` / `App.xaml.cs` – application bootstrap, dependency injection, global exception handling.
- `Views/` – all XAML pages and the main shell window.
- `ViewModels/` – view models for each page plus root `MainViewModel`.
- `Models/` – POCOs for projects, tasks, logs, API keys, etc., mapped to Supabase tables.
- `Services/` – auth, dashboard, logging, health, backup, AI providers.
- `Repositories/` – `ISupabaseRepository` and `SupabaseRepository` for all database calls.
- `Configuration/` – `SupabaseConfig` and `AIConfig` (bound from `appsettings.json`).
- `logs/` – Serilog output, including app-level diagnostics.

Key files:

- `App.xaml.cs` – sets up `Host`, config, HttpClient + Polly policies, and DI for all services and view models.
- `Views/MainWindow.xaml` – main shell (sidebar + header + content, with blur behind login).
- `ViewModels/MainViewModel.cs` – global navigation, auth guard, sidebar profile, global search.
- `Services/AuthService.cs` – Supabase auth (`/auth/v1/token`, `/auth/v1/signup`, `/auth/v1/recover`).
- `Repositories/SupabaseRepository.cs` – REST-based access to Supabase tables with auth / anon tokens.
- `ViewModels/PageViewModels.cs` – all page view models (Dashboard, Projects, ApiVault, ApiTester, Workflows, Reports, Meetings, Logs, IdeasLab, Settings, Login, Register, Profile).
- `supabase_schema.sql` (at repo root) – schema + RLS for all required Supabase tables.

---

## Supabase Integration

### Configuration

Supabase configuration is read from environment variables (preferred) or `appsettings.json`:

- Environment variables:
  - `SUPABASE_URL`
  - `SUPABASE_ANON_KEY`
- `appsettings.json`:

```json
{
  "Supabase": {
    "Url": "https://YOUR-PROJECT-REF.supabase.co",
    "AnonKey": "YOUR_ANON_PUBLIC_KEY"
  },
  "AI": {
    "OpenAIKey": "",
    "AnthropicKey": "",
    "GeminiKey": ""
  }
}
```

`AuthService` and `SupabaseRepository` both:

- Set `HttpClient.BaseAddress` to `Supabase.Url`.
- Add `apikey` header.
- Add `Authorization: Bearer <access_token>` when a user is logged in, or fall back to the anon key.
- Send JSON bodies via `application/json`.

### Auth Endpoints

- **Login** – `POST /auth/v1/token?grant_type=password`
- **Register** – `POST /auth/v1/signup`
- **Password Reset** – `POST /auth/v1/recover`

`AuthService` deserializes into `AuthSession` and stores:

- `access_token`, `refresh_token`, `expires_in`, `expires_at`, and the nested `user`.
- `CurrentUser` (a `UserProfile`) with Id, email, display name, role, and computed initials.

`IsSessionValid` uses the expiry information, with `TryRestoreSessionAsync` maintaining an in-memory session across the app’s lifetime.

### Database Access

All Supabase tables are accessed via `SupabaseRepository`. Important mappings:

- **Projects**
  - Table: `projects`
  - Model: `Project`
  - JSON mapping:
    - `id` → `Project.Id`
    - `user_id` → `Project.OwnerUserId`
    - `name` → `Project.Name`
    - `description` → `Project.Description`
    - `created_at` → `Project.CreatedAt`
  - Methods:
    - `GetProjectsAsync()`
    - `CreateProjectAsync(Project project)`
    - `UpdateProjectAsync(Project project)`
    - `DeleteProjectAsync(string projectId)`

- **API keys**
  - Table: `ApiKeyItems` (or map to `api_keys` if you adjust the repository).
  - Model: `ApiKeyItem`.
  - Methods:
    - `GetApiKeysAsync()`
    - `CreateApiKeyAsync(ApiKeyItem key)`

- Other tables: `TaskItems`, `ProjectNotes`, `ProjectActivities`, `AppLogs`, `AppSettings`, `WorkflowItems`, `MeetingLinks`, `ProjectReports`, `AIUsageRecords`.

`PostAsync<T>` throws detailed `HttpRequestException`s when Supabase returns non-success responses; these are caught in the view models and surfaced as user-friendly but informative error messages.

### Row Level Security (RLS)

RLS policies are defined in `supabase_schema.sql`. Key behavior:

- **Projects**
  - Only the owner (`auth.uid() = user_id`) can see or insert their own projects.
- **Tasks / notes / activities**
  - Only visible and modifiable if they belong to a project where `OwnerUserId = auth.uid()`.
- **API keys, settings, AI usage**
  - Filtered per-user via `UserId`.
- **Logs**
  - All authenticated users can insert and read logs (so the Logs page can show system status).

---

## Features and What Works

### Authentication & Global Navigation

- **Login / Register / Forgot Password**
  - Login and registration are fully wired to Supabase.
  - Registration shows clear error messages when Supabase rejects a signup.
  - Forgot password uses `/auth/v1/recover` and displays a confirmation dialog on success.
  - Login button and Enter key on the PasswordBox both trigger the same `LoginCommand`.
  - Form text and state reset correctly between login and register flows.

- **Session and Auth Guard**
  - `MainViewModel` holds a global `IsAuthenticated` flag and `CurrentViewModel`.
  - On app startup:
    - The Login view is shown.
    - A background attempt (`TryAutoLoginAsync`) tries to restore an in-memory session.
  - Navigation is guarded:
    - Dashboard, Projects, API Vault, API Tester, AI Playground, Workflows, Reports are protected.
    - When unauthenticated, navigation attempts route back to the Login view.
  - On successful login:
    - `IsAuthenticated = true`.
    - `CurrentViewModel = DashboardVM` (`Workspace Dashboard`).

- **User Display**
  - `AuthService` populates `CurrentUser`.
  - `MainViewModel` copies this into `SidebarUserName`, `SidebarUserInitials`, and `SidebarUserRole`.
  - The sidebar avatar and profile area show the logged-in user information.

- **Visual Shell and Focus**
  - When not authenticated:
    - Sidebar and header are blurred using `BlurEffect` style triggers.
    - The login/register card remains sharp and centered.
  - After authentication:
    - Blur is removed and normal navigation is enabled.

### Dashboard

- Uses `IDashboardService` + `SupabaseRepository` to compute:
  - Completed tasks (based on `TaskItems.IsCompleted`).
  - Active projects (based on `Project.Status`).
  - Upcoming meetings (from `MeetingLinks`).
  - AI token usage today (from `AIUsageRecords`).
- Shows “Upcoming Tasks” and “Workflow Library” sections backed by real Supabase data.
- Refresh button reloads all data.
- “Create Report” uses `GenerateDashboardReportCommand` to create and log a dashboard summary report.

### Projects

- **Listing**
  - `ProjectsViewModel` loads projects via `GetProjectsAsync`, using a short-lived cache to avoid unnecessary calls.
  - The list is filtered and refreshed every 5 minutes via a dispatcher timer.

- **Create Project**
  - Creates a `Project` with `Name` “New Project” and description “Generated via UI”.
  - Calls `CreateProjectAsync` → Supabase `projects` table.
  - On success:
    - Adds to the observable `Projects` collection.
    - Logs a “Create Project” entry.
    - Adds a `ProjectActivity` record (“Project Created”).
  - On Supabase error:
    - Catches `HttpRequestException`.
    - Logs full error details.
    - Shows a message dialog including the Supabase response (status code and error JSON).

- **Edit / Delete**
  - Rename uses `UpdateProjectAsync` and logs a “Project Renamed” activity.
  - Delete uses `DeleteProjectAsync` and removes the item from the list with proper logging and activity records.

- **Tasks & Activities**
  - For the selected project:
    - Loads associated `TaskItems` and `ProjectActivities`.
  - Provides:
    - `AddTaskCommand` – creates a “New Task”, associates it with the project, saves to Supabase, logs, and creates an activity.
    - `ToggleTaskCompletedCommand` – toggles the completed state and status, persists to Supabase, logs “Task Completed” or “Task Reopened”, and records an activity.

### API Vault

- `ApiVaultViewModel` loads keys from Supabase via `GetApiKeysAsync()`:
  - Replaces decrypted values with a bullet string for display.
  - Table shows each key’s name, tags, description, and obfuscated key.
- “Add API Key”:
  - Button uses Segoe Fluent icon; command runs `AddKeyAsync()`.
  - Encrypts the raw key via `EncryptionHelper`, saves to Supabase, then appends a display-safe version to the grid.

### API Tester

- Simple HTTP client for arbitrary endpoints:
  - Allows choosing method, URL, and body.
  - Shows formatted JSON or raw content.
  - Displays status code and response time.
  - Logs each test into both `AppLogs` and the Logs page.

### Logs

- `LogsViewModel` reads the latest logs from Supabase and keeps them filterable by level and module.
- “Export CSV” copies the current log view to clipboard and logs the export action itself.

### Ideas Lab, Workflows, Meetings, Settings, Profile

- **Ideas Lab**
  - Hooks into `IAIService` for idea generation (text playground).
  - Includes multiple modes (text, flow diagram, automation suggestions) wired via commands.

- **Workflows**
  - Lists workflows from Supabase and supports adding new ones.
  - “Run” logs a “Run Workflow” entry (placeholder for deeper integration).

- **Meetings**
  - Lists meeting links from Supabase.
  - Allows adding demo meetings (`AddMeetingCommand`) and launching external links (`LaunchMeetingCommand`).

- **Settings**
  - Persisted `AppSettings` (theme, default AI provider, etc.) per user.
  - Loads and saves encrypted API keys for external AI services.
  - Can trigger a backup export via `IBackupService`.

- **Profile**
  - Displays and allows editing the user’s display name.
  - Updates the in-memory user profile and logs the change.
  - Logout clears the session and returns to the login view.

---

## Error Handling and Logging

- **Serilog** writes to:
  - Console.
  - Rolling file under `logs/aihub-log-<date>.txt`.
- `LoggingService` sends structured `AppLog` entries to Supabase for:
  - Auth success / failure.
  - Project, task, workflow, and report actions.
  - API tester calls.
  - Settings and profile updates.
- View models catch and log:
  - Supabase HTTP errors (`HttpRequestException`).
  - Unexpected exceptions when loading data.
- User-facing dialogs:
  - Provide meaningful descriptions (e.g., include Supabase’s status and error JSON for create-project failures).

---

## Running the Application

1. **Install dependencies**

   - .NET 8 SDK
   - A running Supabase project with the schema from `supabase_schema.sql` applied.

2. **Configure Supabase**

   - Either set environment variables:

     ```powershell
     $env:SUPABASE_URL = "https://YOUR-PROJECT-REF.supabase.co"
     $env:SUPABASE_ANON_KEY = "YOUR_ANON_PUBLIC_KEY"
     ```

   - Or edit `appsettings.json` with the same values.

3. **Apply schema**

   - Open the Supabase SQL editor and run `supabase_schema.sql`.

4. **Build & run**

   ```bash
   dotnet build
   dotnet run
   ```

5. **End-to-end validation**

   - Register a user, verify the confirmation email in Supabase, then log in.
   - Confirm:
     - Logged-in user’s name and initials appear in the sidebar.
     - Dashboard tiles load real data.
     - Projects can be created, renamed, and deleted without permission errors.
     - Tasks can be added and completed.
     - API Vault shows keys when present.
     - Logs populate with actions.
     - Icons render correctly and the loading / blur behavior works as expected.

---

## Known Limitations / Next Steps

- Automatic token refresh is not yet implemented; when a Supabase session expires, the user may need to log in again.
- API Tester and Workflows are primarily for experimentation; they do not yet orchestrate real backend workflows.
- Some error messages could be further localized or mapped to more user-friendly text depending on common Supabase error shapes.



test
