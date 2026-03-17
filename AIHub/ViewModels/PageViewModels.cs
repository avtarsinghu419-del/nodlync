using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Caching.Memory;
using AIHub.Models;
using AIHub.Services;
using AIHub.Repositories;
using AIHub.Utilities;

namespace AIHub.ViewModels
{
    // ─────────────────────────────────────────────────
    //  DASHBOARD
    // ─────────────────────────────────────────────────
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly IDashboardService _dashboardService;
        private readonly ISupabaseRepository _repo;
        private readonly ILoggingService _logger;

        // Stat cards
        [ObservableProperty] private int    _completedTasksCount;
        [ObservableProperty] private string _completedTasksTrend = "+0%";
        [ObservableProperty] private int    _activeProjectsCount;
        [ObservableProperty] private int    _upcomingMeetingsCount;
        [ObservableProperty] private string _tokensLabel = "0";
        [ObservableProperty] private string _tokensSubLabel = "0% of limit";
        [ObservableProperty] private string _systemStatus = "Stable";

        // Recent tasks widget
        [ObservableProperty] private ObservableCollection<TaskItem> _upcomingTasks = new();

        // Recent workflows widget
        [ObservableProperty] private ObservableCollection<WorkflowItem> _recentWorkflows = new();

        // Loading / empty
        [ObservableProperty] private bool _isLoading;

        public DashboardViewModel(IDashboardService dashboardService, ISupabaseRepository repo, ILoggingService logger)
        {
            _dashboardService = dashboardService;
            _repo = repo;
            _logger = logger;
            _ = LoadAsync();
        }

        [RelayCommand]
        public async Task RefreshAsync() => await LoadAsync();

        [RelayCommand]
        public async Task GenerateDashboardReportAsync()
        {
            try
            {
                var tasks = await _repo.GetTasksAsync();
                var completed = tasks.Where(t => t.Status == "Completed" || t.IsCompleted).Select(t => t.Title).ToList();
                var report = new ProjectReport
                {
                    ProjectName = "Dashboard Summary",
                    CompletedTasks = completed,
                    NextSteps = new() { "Review upcoming tasks", "Plan tomorrow's work" },
                    Blockers = new()
                };
                await _repo.CreateReportAsync(report);
                await _logger.LogAsync("Dashboard Report", "SUCCESS", "Generated dashboard summary report.");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "DashboardViewModel.GenerateDashboardReport");
                System.Windows.MessageBox.Show("Unable to generate report.", "Dashboard", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                // Stats + tasks + workflows in parallel
                var statsTask     = _dashboardService.GetStatsAsync();
                var tasksTask     = _repo.GetTasksAsync();
                var workflowsTask = _repo.GetWorkflowsAsync();

                await Task.WhenAll(statsTask, tasksTask, workflowsTask);

                var stats = statsTask.Result;
                CompletedTasksCount    = stats.CompletedTasksToday;
                ActiveProjectsCount    = stats.ActiveProjectsCount;
                UpcomingMeetingsCount  = stats.UpcomingMeetingsCount;
                TokensLabel    = stats.AITokensLabel;
                TokensSubLabel = $"{Math.Min(100, stats.AITokensToday / 100)}% of limit";
                SystemStatus           = stats.SystemStatus;

                // Upcoming tasks: due in the future, not completed, top 5
                var today = DateTime.UtcNow.Date;
                var upcoming = tasksTask.Result
                    .Where(t => !t.IsCompleted && (t.DueDate == null || t.DueDate.Value.Date >= today))
                    .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
                    .Take(5)
                    .ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpcomingTasks.Clear();
                    foreach (var t in upcoming) UpcomingTasks.Add(t);

                    RecentWorkflows.Clear();
                    foreach (var w in workflowsTask.Result.Take(3)) RecentWorkflows.Add(w);
                });
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "DashboardViewModel.LoadAsync");
            }
            IsLoading = false;
        }
    }
    public partial class ApiVaultViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _db;
        private readonly ILoggingService _logger;

        [ObservableProperty] private ObservableCollection<ApiKeyItem> _apiKeys = new();

        public ApiVaultViewModel(ISupabaseRepository db, ILoggingService logger)
        {
            _db = db; _logger = logger;
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try {
                var items = await _db.GetApiKeysAsync();
                foreach (var item in items) {
                    item.EncryptedValue = "••••••••••••••••";
                    ApiKeys.Add(item);
                }
            }
            catch (Exception ex) { await _logger.LogErrorAsync(ex, "ApiVaultViewModel Load"); }
        }

        [RelayCommand]
        public async Task AddKeyAsync(string rawKey)
        {
            try {
                var enc = EncryptionHelper.Encrypt(rawKey);
                var keyItem = new ApiKeyItem { Name = "New Key", Tags = "Draft", EncryptedValue = enc.EncryptedValue, InitializationVector = enc.InitializationVector };
                var result = await _db.CreateApiKeyAsync(keyItem);
                if (result != null) {
                    result.EncryptedValue = "••••••••••••••••";
                    ApiKeys.Add(result);
                    await _logger.LogAsync("Create API Key", "SUCCESS", "Key added to vault securely.");
                }
            } catch (Exception ex) { await _logger.LogErrorAsync(ex, "ApiVault Setup Key"); }
        }
    }

    public partial class ApiTesterViewModel : ObservableObject
    {
        private readonly HttpClient _http;
        private readonly ISupabaseRepository _db;
        private readonly ILoggingService _logger;

        [ObservableProperty] private string _requestUrl = "https://jsonplaceholder.typicode.com/todos/1";
        [ObservableProperty] private string _requestMethod = "GET";
        [ObservableProperty] private string _responseBody = "";
        [ObservableProperty] private string _statusCode = "";
        [ObservableProperty] private string _responseTime = "";

        public ApiTesterViewModel(HttpClient http, ISupabaseRepository db, ILoggingService logger)
        {
            _http = http; _db = db; _logger = logger;
        }

        [RelayCommand]
        public async Task SendRequestAsync()
        {
            try {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var req = new HttpRequestMessage(new HttpMethod(RequestMethod), RequestUrl);
                var res = await _http.SendAsync(req);
                sw.Stop();
                ResponseTime = $"{sw.ElapsedMilliseconds} ms";
                StatusCode = $"{(int)res.StatusCode} {res.ReasonPhrase}";
                var content = await res.Content.ReadAsStringAsync();

                // Try format JSON
                try {
                    var obj = Newtonsoft.Json.JsonConvert.DeserializeObject(content);
                    ResponseBody = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
                } catch { ResponseBody = content; }

                await _logger.LogAsync("API Tester", "SUCCESS", $"Tested {RequestMethod} {RequestUrl}");

                // Save explicitly
                await _db.CreateLogAsync(new AppLog { Action = "API Request Executed", Details = $"{RequestMethod} {RequestUrl} [{StatusCode}]" });
            } catch (Exception ex) {
                ResponseBody = ex.Message;
                StatusCode = "ERROR";
                await _logger.LogErrorAsync(ex, "API Tester Execute");
            }
        }
    }

    public partial class WorkflowsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _db;
        private readonly ILoggingService _logger;

        [ObservableProperty] private ObservableCollection<WorkflowItem> _workflows = new();

        public WorkflowsViewModel(ISupabaseRepository db, ILoggingService logger)
        {
            _db = db; _logger = logger;
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            var items = await _db.GetWorkflowsAsync();
            foreach (var item in items) Workflows.Add(item);
        }

        [RelayCommand]
        public async Task AddWorkflowAsync()
        {
            var w = new WorkflowItem { Title = "Auto Gen Workflow", Category = "General", Description = "Generated via AI automation" };
            var result = await _db.CreateWorkflowAsync(w);
            if (result != null) { Workflows.Add(result); await _logger.LogAsync("Create Workflow", "SUCCESS", result.Title); }
        }

        [RelayCommand]
        public async Task RunWorkflowAsync(WorkflowItem? workflow)
        {
            if (workflow == null) return;
            await _logger.LogAsync("Run Workflow", "SUCCESS", $"Workflow '{workflow.Title}' executed.");
        }
    }

    public partial class ReportsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _db;
        private readonly ILoggingService _logger;

        [ObservableProperty] private ObservableCollection<ProjectReport> _reports = new();
        [ObservableProperty] private ProjectReport? _selectedReport;

        public ReportsViewModel(ISupabaseRepository db, ILoggingService logger)
        {
            _db = db; _logger = logger;
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            var items = await _db.GetReportsAsync();
            foreach (var item in items) Reports.Add(item);
        }

        [RelayCommand]
        public async Task GenerateReportAsync()
        {
            var tasks = await _db.GetTasksAsync();
            var completed = tasks.Where(t => t.Status == "Completed" || t.IsCompleted).Select(t => t.Title).ToList();
            var report = new ProjectReport { ProjectName = "Daily AI Automations", CompletedTasks = completed, NextSteps = new() { "Refactor Core" }, Blockers = new() { "None" } };
            await _db.CreateReportAsync(report);
            Reports.Add(report);
            await _logger.LogAsync("Report Gen", "SUCCESS", "Generated Daily Execution Report.");
        }
    }

    public partial class MeetingsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _db;

        [ObservableProperty] private ObservableCollection<MeetingLink> _meetings = new();

        public MeetingsViewModel(ISupabaseRepository db)
        {
            _db = db;
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            var items = await _db.GetMeetingsAsync();
            foreach (var item in items) Meetings.Add(item);
        }

        [RelayCommand]
        public async Task AddMeetingAsync()
        {
            var meeting = new MeetingLink
            {
                Title = "New Meeting",
                Platform = "Zoom",
                MeetingUrl = "https://example.com/meeting",
                Notes = "Generated from Meetings page."
            };
            var created = await _db.CreateMeetingAsync(meeting);
            if (created != null)
            {
                Meetings.Add(created);
            }
        }

        [RelayCommand]
        public void LaunchMeeting(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Swallow; launching external URLs is best-effort only
            }
        }
    }

    public partial class LogsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _db;
        private readonly ILoggingService _logger;
        private List<AppLog> _allLogs = new();

        [ObservableProperty] private ObservableCollection<AppLog> _logs = new();
        [ObservableProperty] private string _filterLevel = "All";
        [ObservableProperty] private string _filterModule = string.Empty;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private ObservableCollection<string> _levelOptions = new() { "All", "INFO", "ERROR", "SUCCESS", "WARN" };

        public LogsViewModel(ISupabaseRepository db, ILoggingService logger)
        {
            _db = db; _logger = logger;
            _ = LoadDataAsync();
        }

        partial void OnFilterLevelChanged(string value) => ApplyFilter();
        partial void OnFilterModuleChanged(string value) => ApplyFilter();

        [RelayCommand]
        public async Task RefreshAsync() => await LoadDataAsync();

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                _allLogs = (await _db.GetLogsAsync())
                    .OrderByDescending(i => i.Timestamp)
                    .Take(100)
                    .ToList();
                ApplyFilter();
            }
            catch (Exception ex) { await _logger.LogErrorAsync(ex, "LogsViewModel"); }
            IsLoading = false;
        }

        private void ApplyFilter()
        {
            var filtered = _allLogs.AsEnumerable();
            if (FilterLevel != "All" && !string.IsNullOrWhiteSpace(FilterLevel))
                filtered = filtered.Where(l => l.Status?.Equals(FilterLevel, StringComparison.OrdinalIgnoreCase) == true);
            if (!string.IsNullOrWhiteSpace(FilterModule))
                filtered = filtered.Where(l => l.Action?.Contains(FilterModule, StringComparison.OrdinalIgnoreCase) == true);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Clear();
                foreach (var l in filtered) Logs.Add(l);
            });
        }

        [RelayCommand]
        public async Task ExportCsvAsync()
        {
            try
            {
                var csvLines = new List<string> { "Timestamp,Action,Status,Details" };
                foreach (var log in _allLogs)
                {
                    var line = $"{log.Timestamp:o},{log.Action},{log.Status},\"{log.Details?.Replace("\"", "\"\"")}\"";
                    csvLines.Add(line);
                }
                var csv = string.Join(Environment.NewLine, csvLines);
                System.Windows.Clipboard.SetText(csv);
                await _logger.LogAsync("Logs Export", "SUCCESS", "Logs exported to clipboard as CSV.");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "LogsViewModel.ExportCsv");
            }
        }
    }

    public partial class IdeasLabViewModel : ObservableObject
    {
        private readonly IAIService _aiService;
        private readonly ILoggingService _logger;

        [ObservableProperty] private string _selectedProvider = "GPT-4 Turbo";
        [ObservableProperty] private ObservableCollection<string> _providers = new() { "GPT-4 Turbo", "Claude 3 Opus", "DALL-E 3" };
        [ObservableProperty] private string _playgroundText = string.Empty;

        public IdeasLabViewModel(IAIService aiService, ILoggingService logger)
        {
            _aiService = aiService;
            _logger = logger;
        }

        [RelayCommand]
        public async Task GenerateIdeasAsync()
        {
            try {
                PlaygroundText = "Generating...";
                PlaygroundText = await _aiService.GenerateIdeasAsync("App automation via APIs.", SelectedProvider, "mock-key-fetch-later");
            } catch (Exception ex) { await _logger.LogErrorAsync(ex, "Idea Generation"); }
        }

        [RelayCommand]
        public void ClearPlayground()
        {
            PlaygroundText = string.Empty;
        }

        [RelayCommand]
        public void GenerateFlowDiagram()
        {
            PlaygroundText = "Flow diagram generation is not yet implemented, but this button is wired to a command.";
        }

        [RelayCommand]
        public void ShowAutomationIdeas()
        {
            PlaygroundText = "Automation ideas mode selected. Describe the processes you want to automate.";
        }

        [RelayCommand]
        public void ShowProjectIdeas()
        {
            PlaygroundText = "Project ideas mode selected. Tell me about your domain so I can suggest ideas.";
        }
    }

    public partial class LoginViewModel : ObservableObject
    {
        private readonly IAuthService _auth;
        public Action? OnLoginSuccess { get; set; }
        public Action? OnRequestRegister { get; set; }

        [ObservableProperty] private string _email = string.Empty;
        [ObservableProperty] private string _password = string.Empty;
        [ObservableProperty] private bool _rememberMe = true;
        [ObservableProperty] private bool _showPassword;
        [ObservableProperty] private string _loginActionText = "Log In";
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private string _emailValidationMessage = string.Empty;
        [ObservableProperty] private string _passwordValidationMessage = string.Empty;

        public LoginViewModel(IAuthService auth) { _auth = auth; }

        [RelayCommand]
        public async Task LoginAsync()
        {
            EmailValidationMessage = string.Empty;
            PasswordValidationMessage = string.Empty;
            StatusMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Email))
            {
                EmailValidationMessage = "Email is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                PasswordValidationMessage = "Password is required.";
                return;
            }

            LoginActionText = "Authenticating...";

            if (await _auth.LoginAsync(Email, Password, RememberMe))
            {
                ClearFields();
                LoginActionText = "Log In";
                OnLoginSuccess?.Invoke();
            }
            else
            {
                LoginActionText = "Login Failed";
                await Task.Delay(2000);
                LoginActionText = "Log In";
                StatusMessage = "Login failed. Please check your email and password.";
            }
        }

        [RelayCommand]
        public void NavigateRegister() => OnRequestRegister?.Invoke();

        [RelayCommand]
        public async Task ForgotPasswordAsync()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                StatusMessage = "Enter your email first, then choose Forgot Password.";
                return;
            }
            await _auth.SendPasswordResetAsync(Email);
        }

        public void ClearFields()
        {
            Email = string.Empty;
            Password = string.Empty;
            StatusMessage = string.Empty;
            EmailValidationMessage = string.Empty;
            PasswordValidationMessage = string.Empty;
        }
    }

    public partial class RegisterViewModel : ObservableObject
    {
        private readonly IAuthService _auth;
        public Action? OnRequestLogin { get; set; }

        [ObservableProperty] private string _email = string.Empty;
        [ObservableProperty] private string _emailValidationMessage = string.Empty;
        [ObservableProperty] private string _displayName = string.Empty;
        [ObservableProperty] private string _displayNameValidationMessage = string.Empty;
        [ObservableProperty] private string _password = string.Empty;
        [ObservableProperty] private string _passwordValidationMessage = string.Empty;
        [ObservableProperty] private string _confirmPassword = string.Empty;
        [ObservableProperty] private string _confirmPasswordValidationMessage = string.Empty;
        [ObservableProperty] private bool _showPassword;
        [ObservableProperty] private bool _isPasswordMismatch;
        [ObservableProperty] private double _passwordStrengthValue;
        [ObservableProperty] private string _passwordStrengthLabel = string.Empty;
        [ObservableProperty] private string _registerActionText = "Create Account";
        [ObservableProperty] private string _statusMessage = string.Empty;

        public RegisterViewModel(IAuthService auth) { _auth = auth; }

        partial void OnPasswordChanged(string value)
        {
            UpdatePasswordValidation();
        }

        partial void OnConfirmPasswordChanged(string value)
        {
            UpdatePasswordValidation();
        }

        private void UpdatePasswordValidation()
        {
            ComputePasswordStrength(Password);

            if (string.IsNullOrEmpty(Password) && string.IsNullOrEmpty(ConfirmPassword))
            {
                PasswordValidationMessage = string.Empty;
                ConfirmPasswordValidationMessage = string.Empty;
                IsPasswordMismatch = false;
                return;
            }

            if (Password != ConfirmPassword)
            {
                IsPasswordMismatch = true;
                ConfirmPasswordValidationMessage = "Passwords do not match.";
            }
            else
            {
                IsPasswordMismatch = false;
                ConfirmPasswordValidationMessage = string.Empty;
            }
        }

        private void ComputePasswordStrength(string pwd)
        {
            if (string.IsNullOrEmpty(pwd))
            {
                PasswordStrengthValue = 0;
                PasswordStrengthLabel = string.Empty;
                return;
            }

            var score = 0;
            if (pwd.Length >= 8) score += 30;
            if (pwd.Any(char.IsUpper)) score += 20;
            if (pwd.Any(char.IsLower)) score += 20;
            if (pwd.Any(char.IsDigit)) score += 15;
            if (pwd.Any(c => "!@#$%^&*()_+-=[]{}|;:'\",.<>/?".Contains(c))) score += 15;

            score = Math.Min(100, score);
            PasswordStrengthValue = score;

            if (score < 40) PasswordStrengthLabel = "Weak";
            else if (score < 70) PasswordStrengthLabel = "Moderate";
            else PasswordStrengthLabel = "Strong";
        }

        [RelayCommand]
        public async Task RegisterAsync()
        {
            EmailValidationMessage = string.Empty;
            DisplayNameValidationMessage = string.Empty;
            PasswordValidationMessage = string.Empty;
            ConfirmPasswordValidationMessage = string.Empty;
            StatusMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Email))
            {
                EmailValidationMessage = "Email is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayNameValidationMessage = "Display name is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                PasswordValidationMessage = "Password cannot be empty.";
                return;
            }

            if (IsPasswordMismatch)
            {
                ConfirmPasswordValidationMessage = "Passwords do not match.";
                return;
            }

            RegisterActionText = "Registering...";
            StatusMessage = string.Empty;

            if (await _auth.RegisterAsync(Email, Password, DisplayName))
            {
                // Clear fields and hand control back to login
                Email = string.Empty;
                DisplayName = string.Empty;
                Password = string.Empty;
                ConfirmPassword = string.Empty;
                IsPasswordMismatch = false;
                RegisterActionText = "Create Account";
                OnRequestLogin?.Invoke();
            }
            else
            {
                RegisterActionText = "Registration Failed";
                await Task.Delay(2000);
                RegisterActionText = "Create Account";
            }
        }

        [RelayCommand]
        public void NavigateLogin() => OnRequestLogin?.Invoke();
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly ILoggingService _logger;
        private readonly IBackupService _backupService;
        private readonly IAuthService _auth;

        [ObservableProperty] private AppSettings _settingsItem = new AppSettings();
        [ObservableProperty] private string _statusMessage = string.Empty;

        [ObservableProperty] private string _openAIKey = string.Empty;
        [ObservableProperty] private string _anthropicKey = string.Empty;
        [ObservableProperty] private string _geminiKey = string.Empty;

        [ObservableProperty] private bool _isLoading;

        public SettingsViewModel(ISupabaseRepository repo, ILoggingService logger, IBackupService backupService, IAuthService auth)
        {
            _repo = repo;
            _logger = logger;
            _backupService = backupService;
            _auth = auth;
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            IsLoading = true;
            try
            {
                var s = await _repo.GetSettingsAsync();
                if (s != null) SettingsItem = s;
                else SettingsItem = new AppSettings { UserId = _auth.CurrentUser?.Id ?? "" };

                var keys = await _repo.GetApiKeysAsync();
                foreach (var k in keys)
                {
                    if (k.Provider == "OpenAI") OpenAIKey = EncryptionHelper.Decrypt(k.EncryptedValue, k.InitializationVector);
                    else if (k.Provider == "Anthropic") AnthropicKey = EncryptionHelper.Decrypt(k.EncryptedValue, k.InitializationVector);
                    else if (k.Provider == "Gemini") GeminiKey = EncryptionHelper.Decrypt(k.EncryptedValue, k.InitializationVector);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "LoadSettings");
                StatusMessage = "Failed to load settings.";
            }
            IsLoading = false;
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            IsLoading = true;
            StatusMessage = "Saving...";
            try
            {
                SettingsItem.UserId = _auth.CurrentUser?.Id ?? "";
                await _repo.SaveSettingsAsync(SettingsItem);

                await SaveOrUpdateKeyAsync("OpenAI", OpenAIKey);
                await SaveOrUpdateKeyAsync("Anthropic", AnthropicKey);
                await SaveOrUpdateKeyAsync("Gemini", GeminiKey);

                StatusMessage = "Settings saved successfully!";
                await Task.Delay(2000);
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "SaveSettings");
                StatusMessage = "Failed to save settings.";
            }
            IsLoading = false;
        }

        private async Task SaveOrUpdateKeyAsync(string provider, string keyValue)
        {
            if (string.IsNullOrWhiteSpace(keyValue)) return;

            var existingKeys = await _repo.GetApiKeysAsync();
            var matched = existingKeys.FirstOrDefault(k => k.Provider == provider);

            if (matched != null)
            {
                var enc = EncryptionHelper.Encrypt(keyValue);
                matched.EncryptedValue = enc.EncryptedValue;
                matched.InitializationVector = enc.InitializationVector;
            }
            else
            {
                var enc = EncryptionHelper.Encrypt(keyValue);
                await _repo.CreateApiKeyAsync(new ApiKeyItem {
                    Name = provider + " API Key",
                    Provider = provider,
                    EncryptedValue = enc.EncryptedValue,
                    InitializationVector = enc.InitializationVector
                });
            }
        }

        [RelayCommand]
        public async Task ExportBackupAsync()
        {
            IsLoading = true;
            StatusMessage = "Exporting backup...";
            bool success = await _backupService.ExportDataAsync();
            StatusMessage = success ? "Backup exported successfully!" : "Backup export failed.";
            await Task.Delay(2000);
            StatusMessage = string.Empty;
            IsLoading = false;
            await LoadSettingsAsync();
        }
    }

    // ─────────────────────────────────────────────────
    //  PROFILE
    // ─────────────────────────────────────────────────
    public partial class ProfileViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly IAuthService _auth;
        private readonly ILoggingService _logger;
        public Action? OnLogout { get; set; }

        [ObservableProperty] private string _displayName = string.Empty;
        [ObservableProperty] private string _email = string.Empty;
        [ObservableProperty] private string _role = string.Empty;
        [ObservableProperty] private string _initials = "?";
        [ObservableProperty] private string _avatarUrl = string.Empty;
        [ObservableProperty] private string _selectedAvatarPath = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _isSaving;

        public ProfileViewModel(ISupabaseRepository repo, IAuthService auth, ILoggingService logger)
        {
            _repo = repo;
            _auth = auth;
            _logger = logger;
            _ = LoadProfileAsync();
        }

        public async Task LoadProfileAsync()
        {
            var user = _auth.CurrentUser;
            if (user == null) return;

            // Try loading profile from Supabase; if missing, create one
            var profile = await _repo.GetUserProfileAsync(user.Id);
            if (profile == null)
            {
                profile = new UserProfile
                {
                    Id = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    Role = user.Role
                };
                await _repo.SaveUserProfileAsync(profile);
            }

            // Update current user info and UI bindings
            _auth.UpdateCurrentUser(profile);
            DisplayName = profile.DisplayName;
            Email = profile.Email;
            Role = profile.Role;
            AvatarUrl = profile.AvatarUrl;
            Initials = profile.Initials;
        }

        [RelayCommand]
        public async Task SaveProfileAsync()
        {
            if (_auth.CurrentUser == null) return;

            IsSaving = true;
            StatusMessage = "Saving...";

            try
            {
                // If a new avatar file has been selected, upload it first.
                if (!string.IsNullOrWhiteSpace(SelectedAvatarPath) && File.Exists(SelectedAvatarPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(SelectedAvatarPath);
                        var uploadedUrl = await _repo.UploadAvatarAsync(_auth.CurrentUser.Id, stream, SelectedAvatarPath);
                        if (!string.IsNullOrWhiteSpace(uploadedUrl))
                        {
                            AvatarUrl = uploadedUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogErrorAsync(ex, "UploadAvatar");
                        StatusMessage = "Failed to upload avatar.";
                    }
                }

                var profile = new UserProfile
                {
                    Id = _auth.CurrentUser.Id,
                    Email = _auth.CurrentUser.Email,
                    DisplayName = DisplayName,
                    Role = Role,
                    AvatarUrl = AvatarUrl
                };

                var updated = await _repo.SaveUserProfileAsync(profile);
                if (updated != null)
                {
                    _auth.UpdateCurrentUser(updated);
                    Initials = updated.Initials;
                    AvatarUrl = updated.AvatarUrl;
                    StatusMessage = "Profile updated!";
                    await _logger.LogAsync("Profile", "SUCCESS", "Profile updated");
                    SelectedAvatarPath = string.Empty;
                }
                else
                {
                    StatusMessage = "Update failed.";
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "SaveProfile");
                StatusMessage = "Failed to save profile.";
            }
            finally
            {
                await Task.Delay(2000);
                StatusMessage = string.Empty;
                IsSaving = false;
            }
        }

        [RelayCommand]
        public void SelectAvatar()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Avatar Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedAvatarPath = dlg.FileName;
            }
        }

        [RelayCommand]
        public void LogoutCommand()
        {
            _auth.Logout();
            OnLogout?.Invoke();
        }
    }

    // ─────────────────────────────────────────────────
    //  GLOBAL SEARCH
    // ─────────────────────────────────────────────────
    public partial class SearchResultItem : ObservableObject
    {
        public string Title     { get; set; } = string.Empty;
        public string Subtitle  { get; set; } = string.Empty;
        public string Category  { get; set; } = string.Empty;
        public string Icon      { get; set; } = "🔍";
    }
}
