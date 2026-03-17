using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIHub.Models;

namespace AIHub.Repositories
{
    public interface ISupabaseRepository
    {
        Task<List<Project>> GetProjectsAsync(bool forceRefresh = false, CancellationToken ct = default);
        Task<Project?> CreateProjectAsync(Project project, CancellationToken ct = default);
        Task<bool> UpdateProjectAsync(Project project, CancellationToken ct = default);
        Task<bool> DeleteProjectAsync(string projectId, CancellationToken ct = default);
        
        Task<List<TaskItem>> GetTasksAsync(string? projectId = null, CancellationToken ct = default);
        Task<TaskItem?> CreateTaskAsync(TaskItem task, CancellationToken ct = default);
        Task UpdateTaskAsync(TaskItem task, CancellationToken ct = default);
        Task DeleteTaskAsync(string taskId, CancellationToken ct = default);

        Task<List<ProjectNote>> GetProjectNotesAsync(string? projectId = null, CancellationToken ct = default);
        Task<ProjectNote?> CreateProjectNoteAsync(ProjectNote note, CancellationToken ct = default);
        Task DeleteProjectNoteAsync(string noteId, CancellationToken ct = default);

        Task<List<Milestone>> GetMilestonesAsync(string? projectId = null, CancellationToken ct = default);
        Task<Milestone?> CreateMilestoneAsync(Milestone milestone, CancellationToken ct = default);
        Task UpdateMilestoneAsync(Milestone milestone, CancellationToken ct = default);
        Task DeleteMilestoneAsync(string milestoneId, CancellationToken ct = default);

        Task<List<ProjectBlocker>> GetProjectBlockersAsync(string? projectId = null, CancellationToken ct = default);
        Task<ProjectBlocker?> CreateProjectBlockerAsync(ProjectBlocker blocker, CancellationToken ct = default);
        Task UpdateProjectBlockerAsync(ProjectBlocker blocker, CancellationToken ct = default);
        Task DeleteProjectBlockerAsync(string blockerId, CancellationToken ct = default);

        Task<List<ProjectMember>> GetProjectMembersAsync(string? projectId = null, CancellationToken ct = default);
        Task<ProjectMember?> CreateProjectMemberAsync(ProjectMember member, CancellationToken ct = default);
        Task DeleteProjectMemberAsync(string memberId, CancellationToken ct = default);

        Task<List<ApiKeyItem>> GetApiKeysAsync(CancellationToken ct = default);
        Task<ApiKeyItem?> CreateApiKeyAsync(ApiKeyItem key, CancellationToken ct = default);

        Task<List<WorkflowItem>> GetWorkflowsAsync(CancellationToken ct = default);
        Task<WorkflowItem?> CreateWorkflowAsync(WorkflowItem workflow, CancellationToken ct = default);

        Task<List<MeetingLink>> GetMeetingsAsync(CancellationToken ct = default);
        Task<MeetingLink?> CreateMeetingAsync(MeetingLink meeting, CancellationToken ct = default);

        Task<List<AppLog>> GetLogsAsync(CancellationToken ct = default);
        Task CreateLogAsync(AppLog log, CancellationToken ct = default);

        Task<List<ProjectReport>> GetReportsAsync(CancellationToken ct = default);
        Task CreateReportAsync(ProjectReport report, CancellationToken ct = default);

        Task<AppSettings?> GetSettingsAsync(CancellationToken ct = default);
        Task<AppSettings?> SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);

        Task<List<AIUsageRecord>> GetAIUsageAsync(CancellationToken ct = default);
        Task CreateAIUsageAsync(AIUsageRecord record, CancellationToken ct = default);

        Task<List<ProjectActivity>> GetProjectActivitiesAsync(string projectId, CancellationToken ct = default);
        Task CreateProjectActivityAsync(ProjectActivity activity, CancellationToken ct = default);

        // User profile (application-level identity) support
        Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken ct = default);
        Task<UserProfile?> SaveUserProfileAsync(UserProfile profile, CancellationToken ct = default);
        Task<string?> UploadAvatarAsync(string userId, Stream imageStream, string fileName, CancellationToken ct = default);
    }
}
