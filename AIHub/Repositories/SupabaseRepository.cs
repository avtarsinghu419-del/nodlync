using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using AIHub.Models;
using AIHub.Configuration;
using AIHub.Services;

namespace AIHub.Repositories
{
    public class SupabaseRepository : ISupabaseRepository
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly SupabaseConfig _config;
        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly string _storageBucket;

        public SupabaseRepository(HttpClient httpClient, IOptions<SupabaseConfig> config, IAuthService authService)
        {
            _httpClient = httpClient;
            _authService = authService;
            _config = config.Value;

            _supabaseUrl = NormalizeSupabaseUrl(Environment.GetEnvironmentVariable("SUPABASE_URL") ?? _config.Url);
            _supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? _config.AnonKey;
            _storageBucket = (Environment.GetEnvironmentVariable("SUPABASE_STORAGE_BUCKET") ?? _config.StorageBucket ?? "avatars")?.Trim('/');

            if (!string.IsNullOrEmpty(_supabaseUrl) && !string.IsNullOrEmpty(_supabaseAnonKey))
            {
                _httpClient.BaseAddress = new Uri($"{_supabaseUrl}/rest/v1/");
                _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);
                _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
            }
        }

        private void ApplyHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();

            var anonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? _config.AnonKey;
            if (!string.IsNullOrEmpty(anonKey))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", anonKey);
            }

            var token = _authService.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            }

            _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
        }

        private static string NormalizeSupabaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            url = url.Trim();
            if (url.EndsWith("/")) url = url.TrimEnd('/');
            if (url.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
                url = url[..^"/rest/v1".Length];

            return url;
        }

        private string BuildStorageUrl(string bucket, string objectPath, bool isPublic)
        {
            // Ensure bucket and path are valid URL path segments (especially for buckets with spaces or other special chars)
            bucket = (bucket ?? string.Empty).Trim('/').Trim();
            objectPath = (objectPath ?? string.Empty).Trim('/');

            var encodedBucket = Uri.EscapeDataString(bucket);
            var encodedObjectPath = string.Join("/", objectPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

            var prefix = isPublic ? "public/" : string.Empty;
            return $"{_supabaseUrl}/storage/v1/object/{prefix}{encodedBucket}/{encodedObjectPath}";
        }

        private async Task<List<T>> GetListAsync<T>(string endpoint, CancellationToken ct)
        {
            ApplyHeaders();
            if (_httpClient.BaseAddress == null) throw new Exception("HttpClient BaseAddress is not configured.");

            var url = endpoint;
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            Console.WriteLine("SUPABASE REQUEST");
            Console.WriteLine(request.Method + " " + request.RequestUri);
            if (request.Content != null)
            {
                Console.WriteLine(await request.Content.ReadAsStringAsync(ct));
            }

            var response = await _httpClient.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            Console.WriteLine("SUPABASE RESPONSE");
            Console.WriteLine(response.StatusCode);
            Console.WriteLine(json);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Supabase error: " + json);

            return JsonConvert.DeserializeObject<List<T>>(json) ?? new List<T>();
        }

        private async Task<List<T>> GetOptionalListAsync<T>(string endpoint, CancellationToken ct)
        {
            try
            {
                return await GetListAsync<T>(endpoint, ct);
            }
            catch (Exception ex) when (IsRecoverableProjectDataError(ex))
            {
                return new List<T>();
            }
        }

        private static bool IsRecoverableProjectDataError(Exception ex)
        {
            return ex.Message.Contains("PGRST205", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("PGRST204", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("42703", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("42P01", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("42501", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<T?> PostAsync<T>(string endpoint, T data, CancellationToken ct) 
        {
            return await PostPayloadAsync<T>(endpoint, data!, ct);
        }

        private async Task<T?> PostPayloadAsync<T>(string endpoint, object data, CancellationToken ct) 
        {
            ApplyHeaders();
            if (_httpClient.BaseAddress == null) throw new Exception("HttpClient BaseAddress is not configured.");

            var jsonPayload = JsonConvert.SerializeObject(data);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var url = endpoint;

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            Console.WriteLine("SUPABASE REQUEST");
            Console.WriteLine(request.Method + " " + request.RequestUri);
            Console.WriteLine(await request.Content.ReadAsStringAsync(ct));

            var response = await _httpClient.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            Console.WriteLine("SUPABASE RESPONSE");
            Console.WriteLine(response.StatusCode);
            Console.WriteLine(json);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Supabase error: " + json);
            }

            var list = JsonConvert.DeserializeObject<List<T>>(json);
            return list != null && list.Count > 0 ? list[0] : default;
        }

         private async Task PatchAsync<T>(string endpoint, string id, T data, CancellationToken ct)
         {
              await PatchPayloadAsync(endpoint, id, data!, ct);
         }

         private async Task PatchPayloadAsync(string endpoint, string id, object data, CancellationToken ct)
         {
              ApplyHeaders();
              if (_httpClient.BaseAddress == null) throw new Exception("HttpClient BaseAddress is not configured.");

              var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
              var url = $"{endpoint}?id=eq.{id}";

              var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };

              Console.WriteLine("SUPABASE REQUEST");
              Console.WriteLine(request.Method + " " + request.RequestUri);
              Console.WriteLine(await request.Content.ReadAsStringAsync(ct));

              var response = await _httpClient.SendAsync(request, ct);
              var body = await response.Content.ReadAsStringAsync(ct);

              Console.WriteLine("SUPABASE RESPONSE");
              Console.WriteLine(response.StatusCode);
              Console.WriteLine(body);

              if (!response.IsSuccessStatusCode)
                  throw new Exception("Supabase error: " + body);
         }

        private async Task<T?> TryPostCandidatesAsync<T>(string endpoint, IEnumerable<object> payloadCandidates, CancellationToken ct)
        {
            Exception? lastError = null;

            foreach (var payload in payloadCandidates)
            {
                try
                {
                    return await PostPayloadAsync<T>(endpoint, payload, ct);
                }
                catch (Exception ex) when (IsMissingColumnError(ex))
                {
                    lastError = ex;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }

            return default;
        }

        private async Task TryPatchCandidatesAsync(string endpoint, string id, IEnumerable<object> payloadCandidates, CancellationToken ct)
        {
            Exception? lastError = null;

            foreach (var payload in payloadCandidates)
            {
                try
                {
                    await PatchPayloadAsync(endpoint, id, payload, ct);
                    return;
                }
                catch (Exception ex) when (IsMissingColumnError(ex))
                {
                    lastError = ex;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }
        }

        private static bool IsMissingColumnError(Exception ex)
        {
            return ex.Message.Contains("PGRST204", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("Could not find the", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("column", StringComparison.OrdinalIgnoreCase);
        }

        private string CurrentUserId => _authService.CurrentUser?.Id ?? string.Empty;

        private IEnumerable<object> BuildTaskPayloadCandidates(TaskItem task)
        {
            var full = new Dictionary<string, object?>
            {
                ["project_id"] = task.ProjectId,
                ["title"] = task.Title ?? string.Empty,
                ["status"] = task.Status ?? "Pending",
                ["priority"] = task.Priority ?? "Normal",
                ["assigned_user"] = string.IsNullOrWhiteSpace(task.AssignedUser) ? null : task.AssignedUser,
                ["due_date"] = task.DueDate,
                ["notes"] = task.Notes ?? string.Empty,
                ["is_completed"] = task.IsCompleted
            };

            yield return full;
            yield return WithoutKeys(full, "assigned_user");
            yield return WithoutKeys(full, "assigned_user", "priority");
            yield return WithoutKeys(full, "assigned_user", "priority", "notes", "due_date");
            yield return new Dictionary<string, object?>
            {
                ["project_id"] = task.ProjectId,
                ["title"] = task.Title ?? string.Empty,
                ["status"] = task.Status ?? "Pending",
                ["is_completed"] = task.IsCompleted
            };
            yield return new Dictionary<string, object?>
            {
                ["project_id"] = task.ProjectId,
                ["title"] = task.Title ?? string.Empty
            };
        }

        private IEnumerable<object> BuildTaskPatchCandidates(TaskItem task)
        {
            var full = new Dictionary<string, object?>
            {
                ["title"] = task.Title ?? string.Empty,
                ["status"] = task.Status ?? "Pending",
                ["priority"] = task.Priority ?? "Normal",
                ["assigned_user"] = string.IsNullOrWhiteSpace(task.AssignedUser) ? null : task.AssignedUser,
                ["due_date"] = task.DueDate,
                ["notes"] = task.Notes ?? string.Empty,
                ["is_completed"] = task.IsCompleted
            };

            yield return full;
            yield return WithoutKeys(full, "assigned_user");
            yield return WithoutKeys(full, "assigned_user", "priority");
            yield return WithoutKeys(full, "assigned_user", "priority", "notes", "due_date");
            yield return new Dictionary<string, object?>
            {
                ["title"] = task.Title ?? string.Empty,
                ["status"] = task.Status ?? "Pending",
                ["is_completed"] = task.IsCompleted
            };
        }

        private IEnumerable<object> BuildProjectNotePayloadCandidates(ProjectNote note)
        {
            var createdAt = note.CreatedAt == default ? DateTime.UtcNow : note.CreatedAt;
            var content = note.Content ?? string.Empty;

            yield return new Dictionary<string, object?> { ["project_id"] = note.ProjectId, ["content"] = content, ["created_at"] = createdAt };
            yield return new Dictionary<string, object?> { ["project_id"] = note.ProjectId, ["note"] = content, ["created_at"] = createdAt };
            yield return new Dictionary<string, object?> { ["project_id"] = note.ProjectId, ["details"] = content, ["created_at"] = createdAt };
            yield return new Dictionary<string, object?> { ["project_id"] = note.ProjectId, ["text"] = content, ["created_at"] = createdAt };
            yield return new Dictionary<string, object?> { ["project_id"] = note.ProjectId, ["body"] = content, ["created_at"] = createdAt };
        }

        private IEnumerable<object> BuildLogPayloadCandidates(AppLog log)
        {
            var message = string.IsNullOrWhiteSpace(log.Details) ? log.Action : $"{log.Action}: {log.Details}";
            var createdAt = log.Timestamp == default ? DateTime.UtcNow : log.Timestamp;
            var userId = string.IsNullOrWhiteSpace(log.UserId) ? CurrentUserId : log.UserId;

            yield return WithOptionalUserId(new Dictionary<string, object?>
            {
                ["timestamp"] = createdAt,
                ["action"] = log.Action ?? string.Empty,
                ["status"] = log.Status ?? string.Empty,
                ["details"] = log.Details ?? string.Empty
            }, userId);

            yield return new Dictionary<string, object?>
            {
                ["timestamp"] = createdAt,
                ["action"] = log.Action ?? string.Empty,
                ["status"] = log.Status ?? string.Empty,
                ["details"] = log.Details ?? string.Empty
            };

            yield return WithOptionalUserId(new Dictionary<string, object?>
            {
                ["created_at"] = createdAt,
                ["action"] = log.Action ?? string.Empty,
                ["status"] = log.Status ?? string.Empty,
                ["details"] = log.Details ?? string.Empty
            }, userId);

            yield return new Dictionary<string, object?>
            {
                ["timestamp"] = createdAt,
                ["message"] = message,
                ["level"] = log.Status ?? string.Empty,
                ["details"] = log.Details ?? string.Empty
            };

            yield return new Dictionary<string, object?>
            {
                ["created_at"] = createdAt,
                ["message"] = message,
                ["level"] = log.Status ?? string.Empty
            };
        }

        private IEnumerable<object> BuildReportPayloadCandidates(ProjectReport report)
        {
            var userId = string.IsNullOrWhiteSpace(report.UserId) ? CurrentUserId : report.UserId;
            var reportDate = report.ReportDate == default ? DateTime.UtcNow : report.ReportDate;

            yield return WithOptionalUserId(new Dictionary<string, object?>
            {
                ["project_name"] = report.ProjectName ?? string.Empty,
                ["completed_tasks"] = report.CompletedTasks,
                ["next_steps"] = report.NextSteps,
                ["blockers"] = report.Blockers,
                ["report_date"] = reportDate
            }, userId);

            yield return WithOptionalUserId(new Dictionary<string, object?>
            {
                ["project_name"] = report.ProjectName ?? string.Empty,
                ["completed_tasks"] = report.CompletedTasks,
                ["next_steps"] = report.NextSteps,
                ["report_date"] = reportDate
            }, userId);

            yield return new Dictionary<string, object?>
            {
                ["project_name"] = report.ProjectName ?? string.Empty,
                ["completed_tasks"] = report.CompletedTasks,
                ["next_steps"] = report.NextSteps,
                ["report_date"] = reportDate
            };

            yield return new Dictionary<string, object?>
            {
                ["project_name"] = report.ProjectName ?? string.Empty,
                ["summary"] = BuildReportSummary(report),
                ["report_date"] = reportDate
            };

            yield return new Dictionary<string, object?>
            {
                ["project_name"] = report.ProjectName ?? string.Empty,
                ["report_date"] = reportDate
            };
        }

        private IEnumerable<object> BuildProjectActivityPayloadCandidates(ProjectActivity activity)
        {
            var createdAt = activity.CreatedAt == default ? DateTime.UtcNow : activity.CreatedAt;
            var userId = string.IsNullOrWhiteSpace(activity.UserId) ? CurrentUserId : activity.UserId;

            yield return WithOptionalUserId(new Dictionary<string, object?>
            {
                ["project_id"] = activity.ProjectId,
                ["action"] = activity.Action ?? string.Empty,
                ["description"] = activity.Description ?? string.Empty,
                ["created_at"] = createdAt
            }, userId);

            yield return new Dictionary<string, object?>
            {
                ["project_id"] = activity.ProjectId,
                ["action"] = activity.Action ?? string.Empty,
                ["description"] = activity.Description ?? string.Empty,
                ["created_at"] = createdAt
            };

            yield return new Dictionary<string, object?>
            {
                ["project_id"] = activity.ProjectId,
                ["action"] = activity.Action ?? string.Empty,
                ["details"] = activity.Description ?? string.Empty,
                ["created_at"] = createdAt
            };
        }

        private static Dictionary<string, object?> WithoutKeys(Dictionary<string, object?> source, params string[] keysToRemove)
        {
            var clone = new Dictionary<string, object?>(source);
            foreach (var key in keysToRemove)
            {
                clone.Remove(key);
            }

            return clone;
        }

        private static Dictionary<string, object?> WithOptionalUserId(Dictionary<string, object?> payload, string? userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                payload["user_id"] = userId;
            }

            return payload;
        }

        private static string BuildReportSummary(ProjectReport report)
        {
            return string.Join(
                "\n",
                new[]
                {
                    $"Completed: {string.Join(", ", report.CompletedTasks)}",
                    $"Next: {string.Join(", ", report.NextSteps)}",
                    $"Blockers: {string.Join(", ", report.Blockers)}"
                });
        }

         private async Task DeleteAsync(string endpoint, string id, CancellationToken ct)
         {
             ApplyHeaders();
             if (_httpClient.BaseAddress == null) throw new Exception("HttpClient BaseAddress is not configured.");
             if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("ID cannot be null or empty.", nameof(id));

             var url = $"{endpoint}?id=eq.{id}";
             var request = new HttpRequestMessage(HttpMethod.Delete, url);

             Console.WriteLine("SUPABASE REQUEST");
             Console.WriteLine(request.Method + " " + request.RequestUri);
             if (request.Content != null)
             {
                 Console.WriteLine(await request.Content.ReadAsStringAsync(ct));
             }

             var response = await _httpClient.SendAsync(request, ct);
             var body = await response.Content.ReadAsStringAsync(ct);

             Console.WriteLine("SUPABASE RESPONSE");
             Console.WriteLine(response.StatusCode);
             Console.WriteLine(body);

             if (!response.IsSuccessStatusCode)
                 throw new Exception("Supabase error: " + body);
         }

        public async Task<List<Project>> GetProjectsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            ApplyHeaders();

            var response = await _httpClient.GetAsync("projects?select=*", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            Console.WriteLine("SUPABASE RESPONSE");
            Console.WriteLine(response.StatusCode);
            Console.WriteLine(body);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Supabase error: " + body);

            var projects = JsonConvert.DeserializeObject<List<Project>>(body) ?? new List<Project>();
            return projects;
        }
        
        public async Task<Project?> CreateProjectAsync(Project project, CancellationToken ct = default)
        {
            ApplyHeaders();

            var ownerId = _authService.CurrentUser?.Id ?? string.Empty;
            var payload = new
            {
                name = project.Name ?? string.Empty,
                description = project.Description ?? string.Empty,
                status = string.IsNullOrWhiteSpace(project.Status) ? "Active" : project.Status,
                user_id = ownerId
            };

            var json = JsonConvert.SerializeObject(payload);
            Console.WriteLine("SUPABASE REQUEST");
            Console.WriteLine("POST projects");
            Console.WriteLine(json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("projects", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            Console.WriteLine("SUPABASE RESPONSE");
            Console.WriteLine(response.StatusCode);
            Console.WriteLine(body);

            if (response.IsSuccessStatusCode)
            {
                var projects = JsonConvert.DeserializeObject<List<Project>>(body);
                return projects?.FirstOrDefault();
            }

            throw new Exception("Supabase error: " + body);
        }

        public async Task<bool> UpdateProjectAsync(Project project, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(project.Id)) return false;
            await PatchAsync("projects", project.Id, project, ct);
            return true;
        }

        public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken ct = default)
        {
            await DeleteAsync("projects", projectId, ct);
            return true;
        }

        public Task<List<TaskItem>> GetTasksAsync(string? projectId = null, CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(projectId)
                ? "task_items"
                : $"task_items?project_id=eq.{projectId}";

            return GetOptionalListAsync<TaskItem>(endpoint, ct);
        }
        public Task<TaskItem?> CreateTaskAsync(TaskItem task, CancellationToken ct = default) => TryPostCandidatesAsync<TaskItem>("task_items", BuildTaskPayloadCandidates(task), ct);
        public Task UpdateTaskAsync(TaskItem task, CancellationToken ct = default) => TryPatchCandidatesAsync("task_items", task.Id, BuildTaskPatchCandidates(task), ct);
        public Task DeleteTaskAsync(string taskId, CancellationToken ct = default) => DeleteAsync("task_items", taskId, ct);

        public Task<List<ProjectNote>> GetProjectNotesAsync(string? projectId = null, CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(projectId)
                ? "project_notes"
                : $"project_notes?project_id=eq.{projectId}";

            return GetOptionalListAsync<ProjectNote>(endpoint, ct);
        }
        public Task<ProjectNote?> CreateProjectNoteAsync(ProjectNote note, CancellationToken ct = default) => TryPostCandidatesAsync<ProjectNote>("project_notes", BuildProjectNotePayloadCandidates(note), ct);
        public Task DeleteProjectNoteAsync(string noteId, CancellationToken ct = default) => DeleteAsync("project_notes", noteId, ct);

        public Task<List<Milestone>> GetMilestonesAsync(string? projectId = null, CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(projectId)
                ? "project_milestones"
                : $"project_milestones?project_id=eq.{projectId}";

            return GetOptionalListAsync<Milestone>(endpoint, ct);
        }
        public Task<Milestone?> CreateMilestoneAsync(Milestone milestone, CancellationToken ct = default) => PostAsync("project_milestones", milestone, ct);
        public Task UpdateMilestoneAsync(Milestone milestone, CancellationToken ct = default) => PatchAsync("project_milestones", milestone.Id, milestone, ct);
        public Task DeleteMilestoneAsync(string milestoneId, CancellationToken ct = default) => DeleteAsync("project_milestones", milestoneId, ct);

        public Task<List<ProjectBlocker>> GetProjectBlockersAsync(string? projectId = null, CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(projectId)
                ? "project_blockers"
                : $"project_blockers?project_id=eq.{projectId}";

            return GetOptionalListAsync<ProjectBlocker>(endpoint, ct);
        }
        public Task<ProjectBlocker?> CreateProjectBlockerAsync(ProjectBlocker blocker, CancellationToken ct = default) => PostAsync("project_blockers", blocker, ct);
        public Task UpdateProjectBlockerAsync(ProjectBlocker blocker, CancellationToken ct = default) => PatchAsync("project_blockers", blocker.Id, blocker, ct);
        public Task DeleteProjectBlockerAsync(string blockerId, CancellationToken ct = default) => DeleteAsync("project_blockers", blockerId, ct);

        public Task<List<ProjectMember>> GetProjectMembersAsync(string? projectId = null, CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(projectId)
                ? "project_members"
                : $"project_members?project_id=eq.{projectId}";

            return GetOptionalListAsync<ProjectMember>(endpoint, ct);
        }
        public Task<ProjectMember?> CreateProjectMemberAsync(ProjectMember member, CancellationToken ct = default) => PostAsync("project_members", member, ct);
        public Task DeleteProjectMemberAsync(string memberId, CancellationToken ct = default) => DeleteAsync("project_members", memberId, ct);

        public Task<List<ApiKeyItem>> GetApiKeysAsync(CancellationToken ct = default) => GetListAsync<ApiKeyItem>("api_key_items", ct);
        public Task<ApiKeyItem?> CreateApiKeyAsync(ApiKeyItem key, CancellationToken ct = default) => PostAsync("api_key_items", key, ct);

        public Task<List<WorkflowItem>> GetWorkflowsAsync(CancellationToken ct = default) => GetListAsync<WorkflowItem>("workflow_items", ct);
        public Task<WorkflowItem?> CreateWorkflowAsync(WorkflowItem workflow, CancellationToken ct = default) => PostAsync("workflow_items", workflow, ct);

        public Task<List<MeetingLink>> GetMeetingsAsync(CancellationToken ct = default) => GetListAsync<MeetingLink>("meeting_links", ct);
        public Task<MeetingLink?> CreateMeetingAsync(MeetingLink meeting, CancellationToken ct = default) => PostAsync("meeting_links", meeting, ct);

        public Task<List<AppLog>> GetLogsAsync(CancellationToken ct = default) => GetOptionalListAsync<AppLog>("app_logs", ct);
        public async Task CreateLogAsync(AppLog log, CancellationToken ct = default) => await TryPostCandidatesAsync<AppLog>("app_logs", BuildLogPayloadCandidates(log), ct);

        public Task<List<ProjectReport>> GetReportsAsync(CancellationToken ct = default) => GetOptionalListAsync<ProjectReport>("project_reports", ct);
        public async Task CreateReportAsync(ProjectReport report, CancellationToken ct = default) => await TryPostCandidatesAsync<ProjectReport>("project_reports", BuildReportPayloadCandidates(report), ct);

        public async Task<AppSettings?> GetSettingsAsync(CancellationToken ct = default) 
        {
            var list = await GetListAsync<AppSettings>("app_settings", ct);
            return list != null && list.Count > 0 ? list[0] : null;
        }
        
        public async Task<AppSettings?> SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
        {
            var existing = await GetSettingsAsync(ct);
            if (existing != null) 
            {
                await PatchAsync("app_settings", existing.Id, settings, ct);
                return settings;
            }
            return await PostAsync("app_settings", settings, ct);
        }

        public Task<List<AIUsageRecord>> GetAIUsageAsync(CancellationToken ct = default) => GetListAsync<AIUsageRecord>("ai_usage_records", ct);
        public async Task CreateAIUsageAsync(AIUsageRecord record, CancellationToken ct = default) => await PostAsync("ai_usage_records", record, ct);

        public async Task<List<ProjectActivity>> GetProjectActivitiesAsync(string projectId, CancellationToken ct = default)
        {
            return await GetOptionalListAsync<ProjectActivity>($"project_activities?project_id=eq.{projectId}", ct);
        }

        public async Task CreateProjectActivityAsync(ProjectActivity activity, CancellationToken ct = default) => await TryPostCandidatesAsync<ProjectActivity>("project_activities", BuildProjectActivityPayloadCandidates(activity), ct);

        // User profile support
        public async Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId)) return null;
            var profiles = await GetListAsync<UserProfile>($"user_profiles?id=eq.{userId}", ct);
            return profiles != null && profiles.Count > 0 ? profiles[0] : null;
        }

        public async Task<UserProfile?> SaveUserProfileAsync(UserProfile profile, CancellationToken ct = default)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id)) return null;

            try
            {
                // Try updating existing profile first
                await PatchAsync("user_profiles", profile.Id, new { display_name = profile.DisplayName, avatar_url = profile.AvatarUrl, role = profile.Role }, ct);
                return await GetUserProfileAsync(profile.Id, ct);
            }
            catch
            {
                // If update fails (e.g., profile doesn't exist), try insert
                return await PostAsync<UserProfile>("user_profiles", profile, ct);
            }
        }

        public async Task<string?> UploadAvatarAsync(string userId, Stream imageStream, string fileName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || imageStream == null || imageStream.Length == 0) return null;

            var cleanFileName = Path.GetFileName(fileName) ?? "avatar";
            var objectPath = $"{userId}/{cleanFileName}";
            var bucket = string.IsNullOrWhiteSpace(_storageBucket) ? "avatars" : _storageBucket.Trim('/');
            var url = BuildStorageUrl(bucket, objectPath, isPublic: false);

            ApplyHeaders();
            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StreamContent(imageStream)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to upload avatar: {body}");

            return BuildStorageUrl(bucket, objectPath, isPublic: true);
        }
    }
}
