using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIHub.Models;
using AIHub.Services;
using AIHub.Repositories;

namespace AIHub.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _supabaseService;
        private readonly ILoggingService _logger;
        private readonly IAuthService _auth;

        [ObservableProperty] private string _pageTitle = "Workspace Dashboard";
        [ObservableProperty] private string _healthWarning = string.Empty;
        [ObservableProperty] private object _currentViewModel;
        [ObservableProperty] private bool _isAuthenticated;

        // Global search
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private ObservableCollection<SearchResultItem> _searchResults = new();
        [ObservableProperty] private bool _showSearchResults;

        // Sidebar user profile info (bound in MainWindow sidebar)
        [ObservableProperty] private string _sidebarUserName = "Loading...";
        [ObservableProperty] private string _sidebarUserInitials = "?";
        [ObservableProperty] private string _sidebarUserRole = string.Empty;
        [ObservableProperty] private string _sidebarUserAvatarUrl = string.Empty;

        // ViewModels
        public DashboardViewModel  DashboardVM  { get; }
        public ProjectsViewModel   ProjectsVM   { get; }
        public ProjectWorkspaceViewModel ProjectWorkspaceVM { get; }
        public ApiVaultViewModel   ApiVaultVM   { get; }
        public ApiTesterViewModel  ApiTesterVM  { get; }
        public WorkflowsViewModel  WorkflowsVM  { get; }
        public ReportsViewModel    ReportsVM    { get; }
        public MeetingsViewModel   MeetingsVM   { get; }
        public LogsViewModel       LogsVM       { get; }
        public IdeasLabViewModel   IdeasLabVM   { get; }
        public SettingsViewModel   SettingsVM   { get; }
        public ProfileViewModel    ProfileVM    { get; }
        public LoginViewModel      LoginVM      { get; }
        public RegisterViewModel   RegisterVM   { get; }

        public MainViewModel(
            ISupabaseRepository supabaseService,
            ILoggingService logger,
            IAuthService auth,
            DashboardViewModel dashboardVM,
            ProjectsViewModel projectsVM,
            ProjectWorkspaceViewModel projectWorkspaceVM,
            ApiVaultViewModel apiVaultVM,
            ApiTesterViewModel apiTesterVM,
            WorkflowsViewModel workflowsVM,
            ReportsViewModel reportsVM,
            MeetingsViewModel meetingsVM,
            LogsViewModel logsVM,
            IdeasLabViewModel ideasLabVM,
            SettingsViewModel settingsVM,
            ProfileViewModel profileVM,
            LoginViewModel loginVM,
            RegisterViewModel registerVM)
        {
            _supabaseService = supabaseService;
            _logger = logger;
            _auth = auth;

            // Keep sidebar in sync with any profile changes (e.g., avatar updates)
            _auth.CurrentUserChanged += OnCurrentUserChanged;

            DashboardVM = dashboardVM;
            ProjectsVM  = projectsVM;
            ProjectWorkspaceVM = projectWorkspaceVM;
            ApiVaultVM  = apiVaultVM;
            ApiTesterVM = apiTesterVM;
            WorkflowsVM = workflowsVM;
            ReportsVM   = reportsVM;
            MeetingsVM  = meetingsVM;
            LogsVM      = logsVM;
            IdeasLabVM  = ideasLabVM;
            SettingsVM  = settingsVM;
            ProfileVM   = profileVM;
            LoginVM     = loginVM;
            RegisterVM  = registerVM;

            ProjectsVM.OnOpenWorkspaceRequested = project => _ = OpenProjectWorkspaceAsync(project);
            ProjectWorkspaceVM.OnBackRequested = () =>
            {
                CurrentViewModel = ProjectsVM;
                PageTitle = "Projects Module";
                _ = ProjectsVM.ActivateAsync();
            };

            // Wire auth callbacks
            LoginVM.OnLoginSuccess    = async () => await OnLoginSuccessAsync();
            LoginVM.OnRequestRegister = () =>
            {
                // Clear registration form before showing
                RegisterVM.Email = string.Empty;
                RegisterVM.DisplayName = string.Empty;
                CurrentViewModel = RegisterVM;
                PageTitle = "Register";
            };
            RegisterVM.OnRequestLogin = () =>
            {
                // After successful registration, show helper message on login
                LoginVM.ClearFields();
                LoginVM.StatusMessage = "Registration successful. Please verify your email, then log in.";
                CurrentViewModel = LoginVM;
                PageTitle = "Sign In";
            };
            ProfileVM.OnLogout        = () =>
            {
                _auth.Logout();
                IsAuthenticated = false;
                CurrentViewModel = LoginVM;
                PageTitle = "Sign In";
                SidebarUserName = "";
                SidebarUserInitials = "?";
                SidebarUserRole = string.Empty;
                SidebarUserAvatarUrl = string.Empty;
            };

            // Start at login. App startup will attempt to restore any persisted session.
            _currentViewModel = LoginVM;
            PageTitle = "Sign In";
            IsAuthenticated = _auth.IsSessionValid;
        }

        /// <summary>
        /// Attempts to restore a previously saved session and, if successful, transitions into the authenticated state.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                bool valid = await _auth.TryRestoreSessionAsync();
                if (valid)
                {
                    await OnLoginSuccessAsync();
                }
            }
            catch
            {
                // Ignore; user will stay on login screen.
            }
        }

        private async Task OnLoginSuccessAsync()
        {
            var user = _auth.CurrentUser;
            if (user != null)
            {
                // Refresh profile from server (if it exists)
                try
                {
                    var profile = await _supabaseService.GetUserProfileAsync(user.Id);
                    if (profile != null)
                    {
                        _auth.UpdateCurrentUser(profile);
                    }
                }
                catch
                {
                    // Ignore; fallback to current cached user
                }

                user = _auth.CurrentUser;
                SidebarUserName     = user?.DisplayName ?? "";
                SidebarUserInitials = user?.Initials ?? "?";
                SidebarUserRole     = user?.Role ?? string.Empty;
                SidebarUserAvatarUrl = user?.AvatarUrl ?? string.Empty;
            }

            IsAuthenticated = true;

            // After login, always show the dashboard directly (bypass guard)
            CurrentViewModel = DashboardVM;
            PageTitle = "Workspace Dashboard";

            ProfileVM.OnLogout  = () =>
            {
                _auth.Logout();
                IsAuthenticated = false;
                CurrentViewModel = LoginVM;
                PageTitle = "Sign In";
                SidebarUserName = "";
                SidebarUserInitials = "?";
                SidebarUserRole = string.Empty;
                SidebarUserAvatarUrl = string.Empty;
            };

            // Ensure profile view is refreshed after a successful login
            await ProfileVM.LoadProfileAsync();
        }

        // ── Navigation ─────────────────────────────────────────────────
        [RelayCommand]
        private void Navigate(string page)
        {
            // Global authentication guard: block protected pages while unauthenticated
            bool authed = _auth.IsSessionValid || IsAuthenticated;
            bool isProtected = page is "Dashboard" or "Projects" or "ApiVault" or "ApiTester" or "IdeasLab" or "Workflows" or "Reports";

            if (!authed && isProtected)
            {
                CurrentViewModel = LoginVM;
                PageTitle = "Sign In";
                return;
            }

            switch (page)
            {
                case "Dashboard": CurrentViewModel = DashboardVM; PageTitle = "Workspace Dashboard"; break;
                case "Projects":
                    CurrentViewModel = ProjectsVM;
                    PageTitle = "Projects Module";
                    _ = ProjectsVM.ActivateAsync();
                    break;
                case "ApiVault":  CurrentViewModel = ApiVaultVM;  PageTitle = "API Vault";             break;
                case "ApiTester": CurrentViewModel = ApiTesterVM; PageTitle = "API Tester";            break;
                case "IdeasLab":  CurrentViewModel = IdeasLabVM;  PageTitle = "AI Playground";        break;
                case "Workflows": CurrentViewModel = WorkflowsVM; PageTitle = "Workflows Library";     break;
                case "Reports":   CurrentViewModel = ReportsVM;   PageTitle = "Reports System";        break;
                case "Meetings":  CurrentViewModel = MeetingsVM;  PageTitle = "Meetings Hub";          break;
                case "Logs":      CurrentViewModel = LogsVM;      PageTitle = "System Logs";           break;
                case "Settings":  CurrentViewModel = SettingsVM;  PageTitle = "Settings";              break;
                case "Profile":   CurrentViewModel = ProfileVM;   PageTitle = "My Profile";            break;
            }
        }

        private void OnCurrentUserChanged(object? sender, UserProfile? user)
        {
            SidebarUserName = user?.DisplayName ?? string.Empty;
            SidebarUserInitials = user?.Initials ?? "?";
            SidebarUserRole = user?.Role ?? string.Empty;
            SidebarUserAvatarUrl = user?.AvatarUrl ?? string.Empty;
        }

        [RelayCommand]
        private async Task QuickCreateProjectAsync()
        {
            bool authed = _auth.IsSessionValid || IsAuthenticated;
            if (!authed)
            {
                CurrentViewModel = LoginVM;
                PageTitle = "Sign In";
                return;
            }

            CurrentViewModel = ProjectsVM;
            PageTitle = "Projects Module";
            await ProjectsVM.ActivateAsync();
            ProjectsVM.BeginCreateProject();
        }

        private async Task OpenProjectWorkspaceAsync(Models.Project project)
        {
            CurrentViewModel = ProjectWorkspaceVM;
            PageTitle = $"{project.Name} Workspace";
            await ProjectWorkspaceVM.LoadProjectAsync(project);
        }

        // ── Global Search ──────────────────────────────────────────────
        [RelayCommand]
        public async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) { ShowSearchResults = false; SearchResults.Clear(); return; }
            try
            {
                var projectsTask  = _supabaseService.GetProjectsAsync();
                var tasksTask     = _supabaseService.GetTasksAsync();
                var workflowsTask = _supabaseService.GetWorkflowsAsync();
                await Task.WhenAll(projectsTask, tasksTask, workflowsTask);

                var q = SearchQuery.ToLower();
                var results = new System.Collections.Generic.List<SearchResultItem>();

                results.AddRange(projectsTask.Result
                    .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new SearchResultItem { Title = p.Name, Subtitle = p.Description, Category = "Project", Icon = "📂" }));

                results.AddRange(tasksTask.Result
                    .Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new SearchResultItem { Title = t.Title, Subtitle = $"Status: {t.Status}", Category = "Task", Icon = "✅" }));

                results.AddRange(workflowsTask.Result
                    .Where(w => w.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Select(w => new SearchResultItem { Title = w.Title, Subtitle = w.Description, Category = "Workflow", Icon = "⚙️" }));

                SearchResults.Clear();
                foreach (var r in results.Take(8)) SearchResults.Add(r);
                ShowSearchResults = SearchResults.Any();
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "GlobalSearch");
            }
        }

        [RelayCommand]
        public void ClearSearch() { SearchQuery = string.Empty; SearchResults.Clear(); ShowSearchResults = false; }
    }
}
