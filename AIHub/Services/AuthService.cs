using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AIHub.Models;
using AIHub.Configuration;
using AIHub.Utilities;
using System.Windows;

namespace AIHub.Services
{
    public interface IAuthService
    {
        AuthSession? CurrentSession { get; }
        UserProfile? CurrentUser { get; }
        bool IsSessionValid { get; }
        string GetAccessToken();
        Task<bool> LoginAsync(string email, string password, bool persistSession = true, CancellationToken ct = default);
        Task<bool> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);
        Task<bool> TryRestoreSessionAsync(CancellationToken ct = default);
        Task<bool> UpdateDisplayNameAsync(string newName, CancellationToken ct = default);
        Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default);
        void UpdateCurrentUser(UserProfile userProfile);
        void Logout();

        /// <summary>
        /// Raised when the current user profile changes (e.g., avatar, name, role, etc.).
        /// </summary>
        event EventHandler<UserProfile?>? CurrentUserChanged;
    }

    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AuthService> _logger;
        private readonly SupabaseConfig _config;
        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly string _sessionFilePath;
        private DateTime _sessionExpiry = DateTime.MinValue;

        public AuthSession? CurrentSession { get; private set; }
        public UserProfile? CurrentUser { get; private set; }
        public bool IsSessionValid => CurrentSession != null && DateTime.UtcNow < _sessionExpiry;

        public AuthService(HttpClient httpClient, IOptions<SupabaseConfig> config, ILogger<AuthService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config.Value;

            _supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? _config.Url;
            _supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? _config.AnonKey;

            if (!string.IsNullOrEmpty(_supabaseUrl))
            {
                _httpClient.BaseAddress = new Uri(_supabaseUrl);
            }

            // Store session data securely in app data so the user stays logged in across restarts
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localFolder = Path.Combine(appData, "AIHub");
            Directory.CreateDirectory(localFolder);
            _sessionFilePath = Path.Combine(localFolder, "authsession.dat");

            ConfigureAnonymousHeaders();
        }

        public async Task<bool> LoginAsync(string email, string password, bool persistSession = true, CancellationToken ct = default)
        {
            try
            {
                ConfigureAnonymousHeaders();

                var payload = new { email, password };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/v1/token?grant_type=password", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    CurrentSession = JsonConvert.DeserializeObject<AuthSession>(json);
                    
                    if (CurrentSession?.user != null)
                    {
                        // Prefer absolute expiry when available, otherwise fallback to relative
                        if (CurrentSession.expires_at > 0)
                        {
                            _sessionExpiry = DateTimeOffset.FromUnixTimeSeconds(CurrentSession.expires_at).UtcDateTime;
                        }
                        else
                        {
                            _sessionExpiry = DateTime.UtcNow.AddSeconds(CurrentSession.expires_in > 0 ? CurrentSession.expires_in : 3600);
                        }
                        ApplyAuthHeader();
                        CurrentUser = new UserProfile
                        {
                            Id = CurrentSession.user.id,
                            Email = CurrentSession.user.email,
                            DisplayName = email.Split('@')[0],
                            Role = string.IsNullOrWhiteSpace(CurrentSession.user.role) ? "Viewer" : CurrentSession.user.role
                        };

                        if (persistSession)
                        {
                            SaveSessionToDisk();
                        }

                        _logger.LogInformation("User {Email} logged in successfully.", email);
                        return true;
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Login failed for {Email}. Status: {StatusCode}. Body: {ErrorBody}", email, response.StatusCode, errorBody);
                    MessageBox.Show("Unable to log in. Please check your credentials.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LoginAsync failed for {Email}", email);
                MessageBox.Show("Unable to log in due to a network or server error.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default)
        {
            try
            {
                ConfigureAnonymousHeaders();

                var payload = new { email, password, data = new { display_name = displayName } };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/v1/signup", content, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("User {Email} registered successfully.", email);
                    return true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Registration failed for {Email}. Status: {StatusCode}. Body: {ErrorBody}", email, response.StatusCode, errorBody);
                    MessageBox.Show("Unable to register user.", "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RegisterAsync failed for {Email}", email);
                MessageBox.Show("Unable to register due to a network or server error.", "Registration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default)
        {
            try
            {
                ConfigureAnonymousHeaders();

                var payload = new { email };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/v1/recover", content, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Password reset email requested for {Email}.", email);
                    MessageBox.Show("If an account exists for this email, a password reset link has been sent.", "Password Reset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Password reset failed for {Email}. Status: {StatusCode}. Body: {ErrorBody}", email, response.StatusCode, errorBody);
                MessageBox.Show("Unable to start password reset. Please check the email address.", "Password Reset Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendPasswordResetAsync failed for {Email}", email);
                MessageBox.Show("Unable to start password reset due to a network or server error.", "Password Reset Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
        {
            if (IsSessionValid) return true;

            var session = LoadSessionFromDisk();
            if (session == null) return false;

            CurrentSession = session;
            SetExpiryFromSession();

            if (CurrentSession?.user != null)
            {
                CurrentUser = new UserProfile
                {
                    Id = CurrentSession.user.id,
                    Email = CurrentSession.user.email,
                    DisplayName = CurrentSession.user.email.Split('@')[0],
                    Role = string.IsNullOrWhiteSpace(CurrentSession.user.role) ? "Viewer" : CurrentSession.user.role
                };
            }

            ApplyAuthHeader();

            if (IsSessionValid)
            {
                _logger.LogInformation("Restored session from disk for {Email}.", CurrentUser?.Email);
                return true;
            }

            // Token expired; attempt refresh if possible
            if (!string.IsNullOrEmpty(CurrentSession?.refresh_token))
            {
                _logger.LogInformation("Session expired; attempting refresh.");
                if (await RefreshSessionAsync(ct))
                {
                    return true;
                }
            }

            // Failed to restore; clear saved session
            DeleteSavedSession();
            CurrentSession = null;
            CurrentUser = null;
            _sessionExpiry = DateTime.MinValue;
            ConfigureAnonymousHeaders();
            return false;
        }

        private void SetExpiryFromSession()
        {
            if (CurrentSession == null)
            {
                _sessionExpiry = DateTime.MinValue;
                return;
            }

            if (CurrentSession.expires_at > 0)
            {
                _sessionExpiry = DateTimeOffset.FromUnixTimeSeconds(CurrentSession.expires_at).UtcDateTime;
            }
            else
            {
                _sessionExpiry = DateTime.UtcNow.AddSeconds(CurrentSession.expires_in > 0 ? CurrentSession.expires_in : 3600);
            }
        }

        private void SaveSessionToDisk()
        {
            try
            {
                if (CurrentSession == null) return;

                var json = JsonConvert.SerializeObject(CurrentSession);
                var encrypted = EncryptionHelper.Encrypt(json);
                File.WriteAllText(_sessionFilePath, JsonConvert.SerializeObject(encrypted));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist auth session to disk.");
            }
        }

        private AuthSession? LoadSessionFromDisk()
        {
            try
            {
                if (!File.Exists(_sessionFilePath)) return null;

                var raw = File.ReadAllText(_sessionFilePath);
                var encrypted = JsonConvert.DeserializeObject<EncryptedData>(raw);
                if (encrypted == null) return null;

                var decrypted = EncryptionHelper.Decrypt(encrypted.EncryptedValue, encrypted.InitializationVector);
                if (string.IsNullOrWhiteSpace(decrypted)) return null;

                return JsonConvert.DeserializeObject<AuthSession>(decrypted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load saved auth session.");
                return null;
            }
        }

        private async Task<bool> RefreshSessionAsync(CancellationToken ct = default)
        {
            if (CurrentSession == null || string.IsNullOrEmpty(CurrentSession.refresh_token))
                return false;

            try
            {
                ConfigureAnonymousHeaders();
                var payload = new { refresh_token = CurrentSession.refresh_token };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/auth/v1/token?grant_type=refresh_token", content, ct);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync(ct);
                CurrentSession = JsonConvert.DeserializeObject<AuthSession>(json);
                if (CurrentSession?.user == null) return false;

                SetExpiryFromSession();
                ApplyAuthHeader();
                CurrentUser = new UserProfile
                {
                    Id = CurrentSession.user.id,
                    Email = CurrentSession.user.email,
                    DisplayName = CurrentSession.user.email.Split('@')[0],
                    Role = string.IsNullOrWhiteSpace(CurrentSession.user.role) ? "Viewer" : CurrentSession.user.role
                };

                SaveSessionToDisk();
                _logger.LogInformation("Refreshed session for {Email}.", CurrentUser.Email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh auth session.");
                return false;
            }
        }

        private void DeleteSavedSession()
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                    File.Delete(_sessionFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete saved auth session.");
            }
        }

        public Task<bool> UpdateDisplayNameAsync(string newName, CancellationToken ct = default)
        {
            if (CurrentUser == null || CurrentSession == null) return Task.FromResult(false);
            try
            {
                CurrentUser.DisplayName = newName;
                _logger.LogInformation("Display name updated to {DisplayName}.", newName);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateDisplayName failed.");
                return Task.FromResult(false);
            }
        }

        public event EventHandler<UserProfile?>? CurrentUserChanged;

        public void UpdateCurrentUser(UserProfile userProfile)
        {
            CurrentUser = userProfile;
            CurrentUserChanged?.Invoke(this, CurrentUser);
        }

        public void Logout()
        {
            if (CurrentSession != null)
            {
                _logger.LogInformation("User {Email} logged out.", CurrentSession.user?.email);
            }
            CurrentSession = null;
            CurrentUser = null;
            _sessionExpiry = DateTime.MinValue;
            DeleteSavedSession();
            ConfigureAnonymousHeaders();
            CurrentUserChanged?.Invoke(this, null);
        }

        public string GetAccessToken()
        {
            return CurrentSession?.access_token ?? string.Empty;
        }

        private void ApplyAuthHeader()
        {
            ConfigureAnonymousHeaders();

            if (!string.IsNullOrEmpty(CurrentSession?.access_token))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {CurrentSession.access_token}");
            }
        }

        private void ConfigureAnonymousHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();

            if (!string.IsNullOrWhiteSpace(_supabaseAnonKey))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);
            }
        }
    }
}
