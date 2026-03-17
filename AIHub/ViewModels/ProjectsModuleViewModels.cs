using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIHub.Models;
using AIHub.Repositories;
using AIHub.Services;
using Newtonsoft.Json;

namespace AIHub.ViewModels
{
    public partial class ProjectsViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly IAuthService _auth;
        private readonly ILoggingService _logger;

        [ObservableProperty] private ObservableCollection<Project> _projects = new();
        [ObservableProperty] private Project? _selectedProject;
        [ObservableProperty] private Project _newProject = new();
        [ObservableProperty] private bool _isCreateMode = true;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isEmpty;

        public ObservableCollection<string> StatusOptions { get; } = new()
        {
            "Active",
            "Completed",
            "On Hold"
        };

        public IRelayCommand NewProjectCommand { get; }
        public IAsyncRelayCommand CreateProjectCommand { get; }
        public IAsyncRelayCommand UpdateProjectCommand { get; }
        public IRelayCommand RefreshProjectsCommand { get; }
        public ICommand OpenWorkspaceCommand { get; }
        public Action<Project>? OnOpenWorkspaceRequested { get; set; }

        public ProjectsViewModel(ISupabaseRepository repo, IAuthService auth, ILoggingService logger)
        {
            _repo = repo;
            _auth = auth;
            _logger = logger;

            NewProjectCommand = new RelayCommand(StartCreateProject);
            CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync);
            UpdateProjectCommand = new AsyncRelayCommand(UpdateProjectAsync);
            RefreshProjectsCommand = new AsyncRelayCommand(() => LoadProjectsInternalAsync(true));
            OpenWorkspaceCommand = new RelayCommand<Project?>(OpenWorkspace, project => project != null);

            StartCreateProject();
            _ = LoadProjectsInternalAsync(false);
        }

        [RelayCommand]
        private async Task LoadProjectsAsync()
        {
            await LoadProjectsInternalAsync(false);
        }

        public Task ActivateAsync(bool forceRefresh = true)
        {
            return LoadProjectsInternalAsync(forceRefresh);
        }

        private async Task LoadProjectsInternalAsync(bool forceRefresh, string? selectProjectId = null)
        {
            IsLoading = true;
            try
            {
                var currentSelectionId = selectProjectId ?? SelectedProject?.Id;
                var items = await _repo.GetProjectsAsync(forceRefresh);
                Console.WriteLine("Projects fetched: " + JsonConvert.SerializeObject(items));

                Projects = new ObservableCollection<Project>(items ?? new());

                IsEmpty = Projects.Count == 0;

                if (IsEmpty)
                {
                    StartCreateProject();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(currentSelectionId))
                {
                    var matchingProject = Projects.FirstOrDefault(p => p.Id == currentSelectionId);
                    if (matchingProject != null)
                    {
                        SelectedProject = matchingProject;
                        IsCreateMode = false;
                        return;
                    }
                }

                if (IsCreateMode)
                {
                    return;
                }

                SelectedProject = Projects.First();
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "LoadProjects");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void StartCreateProject()
        {
            Console.WriteLine("NewProjectCommand clicked");
            IsCreateMode = true;
            SelectedProject = null;
            NewProject = new Project
            {
                Status = "Active"
            };
            NotifyWorkspaceCommand();
        }

        public void BeginCreateProject()
        {
            StartCreateProject();
        }

        private void OpenWorkspace(Project? project)
        {
            if (project == null)
            {
                return;
            }

            OnOpenWorkspaceRequested?.Invoke(project);
        }

        private void NotifyWorkspaceCommand()
        {
            if (OpenWorkspaceCommand is RelayCommand<Project?> command)
            {
                command.NotifyCanExecuteChanged();
            }
        }

        private async Task CreateProjectAsync()
        {
            if (IsLoading || !IsCreateMode)
            {
                return;
            }

            IsLoading = true;
            try
            {
                Console.WriteLine("CreateProjectCommand clicked");
                var projectToCreate = NewProject;
                if (projectToCreate == null || string.IsNullOrWhiteSpace(projectToCreate.Name))
                {
                    MessageBox.Show("Project name is required.", "Projects", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                projectToCreate.Name = projectToCreate.Name.Trim();
                projectToCreate.Description = (projectToCreate.Description ?? string.Empty).Trim();
                projectToCreate.Status = Project.ToDisplayStatus(projectToCreate.Status);
                projectToCreate.OwnerUserId = _auth.CurrentUser?.Id ?? string.Empty;

                Console.WriteLine("Payload sent: " + JsonConvert.SerializeObject(new
                {
                    projectToCreate.Name,
                    projectToCreate.Description,
                    projectToCreate.Status,
                    projectToCreate.OwnerUserId
                }));

                var created = await _repo.CreateProjectAsync(projectToCreate);
                if (created == null)
                {
                    throw new InvalidOperationException("Supabase did not return the created project.");
                }

                // Keep list in sync immediately, then select the created project into edit mode.
                Projects.Insert(0, created);
                Console.WriteLine("Response received: " + JsonConvert.SerializeObject(created));
                IsEmpty = Projects.Count == 0;
                SelectedProject = created;
                IsCreateMode = false;
                Console.WriteLine("Projects list updated");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "CreateProject");
                MessageBox.Show($"Unable to create the project: {ex.Message}", "Projects", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpdateProjectAsync()
        {
            if (IsLoading || IsCreateMode || SelectedProject == null || string.IsNullOrWhiteSpace(SelectedProject.Name))
            {
                return;
            }

            IsLoading = true;
            try
            {
                SelectedProject.Name = SelectedProject.Name.Trim();
                SelectedProject.Description = (SelectedProject.Description ?? string.Empty).Trim();
                SelectedProject.Status = Project.ToDisplayStatus(SelectedProject.Status);

                var updatedSuccessfully = await _repo.UpdateProjectAsync(SelectedProject);
                if (!updatedSuccessfully)
                {
                    throw new InvalidOperationException("The project could not be updated.");
                }

                await _logger.LogAsync("Update Project", "SUCCESS", $"Updated project '{SelectedProject.Name}'.");
                await LoadProjectsInternalAsync(true, SelectedProject.Id);
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "UpdateProject");
                MessageBox.Show($"Unable to update the project: {ex.Message}", "Projects", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task DeleteProjectAsync(Project? project)
        {
            if (project == null)
            {
                return;
            }

            var confirmation = MessageBox.Show(
                $"Delete '{project.Name}'?\n\nThis action cannot be undone.",
                "Delete Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            IsLoading = true;
            try
            {
                var currentSelectedId = SelectedProject?.Id;
                var fallbackSelectionId = currentSelectedId == project.Id
                    ? Projects.FirstOrDefault(p => p.Id != project.Id)?.Id
                    : currentSelectedId;

                var deleted = await _repo.DeleteProjectAsync(project.Id);
                if (!deleted)
                {
                    throw new InvalidOperationException("The project could not be deleted.");
                }

                await _logger.LogAsync("Delete Project", "SUCCESS", $"Deleted project '{project.Name}'.");
                await LoadProjectsInternalAsync(true, fallbackSelectionId);

                if (Projects.Count == 0)
                {
                    StartCreateProject();
                }

                NotifyWorkspaceCommand();
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "DeleteProject");
                MessageBox.Show($"Unable to delete the project: {ex.Message}", "Projects", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSelectedProjectChanged(Project? value)
        {
            if (value != null)
            {
                value.Status = Project.ToDisplayStatus(value.Status);
                IsCreateMode = false;
            }

            NotifyWorkspaceCommand();
        }

        partial void OnNewProjectChanged(Project value)
        {
            if (value != null)
            {
                value.Status = Project.ToDisplayStatus(value.Status);
            }
        }
    }

    public partial class ProjectEditorViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly IAuthService _auth;
        private readonly ILoggingService _logger;

        [ObservableProperty] private Project? _activeProject;
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private string _status = "Active";
        [ObservableProperty] private bool _isCreateMode = true;
        [ObservableProperty] private bool _isSaving;

        public ObservableCollection<string> StatusOptions { get; } = new()
        {
            "Active",
            "Completed",
            "On Hold"
        };

        public Func<ProjectEditorViewModel, Task>? OnSaveRequested { get; set; }
        public Func<ProjectEditorViewModel, Task>? OnDeleteRequested { get; set; }

        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public string FormTitle => IsCreateMode ? "Create Project" : Name;
        public string FormSubtitle => IsCreateMode
            ? "Use this panel to create a new project."
            : "Edit the selected project in this panel.";
        public string SaveButtonText => IsCreateMode ? "Create Project" : "Save Changes";
        public string CreatedAtText => ActiveProject == null ? string.Empty : $"Created {ActiveProject.CreatedAt:MMM dd, yyyy}";
        public bool HasExistingProject => ActiveProject != null && !IsCreateMode;
        public bool CanSave => !IsSaving && !string.IsNullOrWhiteSpace(Name);
        public bool CanDelete => HasExistingProject && !IsSaving;
        public string StatusBadgeText => Project.ToDisplayStatus(Status);

        public ProjectEditorViewModel(ISupabaseRepository repo, IAuthService auth, ILoggingService logger)
        {
            _repo = repo;
            _auth = auth;
            _logger = logger;

            SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => CanSave);
            DeleteCommand = new AsyncRelayCommand(ExecuteDeleteAsync, () => CanDelete);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        public void BeginCreate()
        {
            ActiveProject = null;
            Name = string.Empty;
            Description = string.Empty;
            Status = "Active";
            IsCreateMode = true;
        }

        public void LoadProject(Project project)
        {
            ActiveProject = project;
            Name = project.Name;
            Description = project.Description;
            Status = Project.ToDisplayStatus(project.Status);
            IsCreateMode = false;
        }

        partial void OnNameChanged(string value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(FormTitle));
        }

        partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusBadgeText));

        partial void OnIsSavingChanged(bool value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanDelete));
        }

        partial void OnIsCreateModeChanged(bool value)
        {
            DeleteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(FormSubtitle));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(HasExistingProject));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(StatusBadgeText));
        }

        partial void OnActiveProjectChanged(Project? value)
        {
            OnPropertyChanged(nameof(CreatedAtText));
            OnPropertyChanged(nameof(HasExistingProject));
            OnPropertyChanged(nameof(CanDelete));
        }

        private async Task ExecuteSaveAsync()
        {
            if (IsSaving || OnSaveRequested == null)
            {
                return;
            }

            IsSaving = true;
            try
            {
                await OnSaveRequested(this);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task ExecuteDeleteAsync()
        {
            if (IsSaving || OnDeleteRequested == null || ActiveProject == null)
            {
                return;
            }

            IsSaving = true;
            try
            {
                await OnDeleteRequested(this);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void ExecuteCancel()
        {
            if (ActiveProject != null && !IsCreateMode)
            {
                LoadProject(ActiveProject);
                return;
            }

            BeginCreate();
        }
    }

    public partial class ProjectDetailViewModel : ObservableObject
    {
        private readonly ILoggingService _logger;

        public Project Project { get; }

        public TasksViewModel TasksVM { get; }
        public NotesViewModel NotesVM { get; }
        public MilestonesViewModel MilestonesVM { get; }
        public BlockersViewModel BlockersVM { get; }
        public MembersViewModel MembersVM { get; }
        public ProjectReportsTabViewModel ReportsVM { get; }

        [ObservableProperty] private bool _isLoading;

        public ProjectDetailViewModel(ISupabaseRepository repo, IAuthService auth, ILoggingService logger, Project project)
        {
            _logger = logger;
            Project = project;

            TasksVM = new TasksViewModel(repo, project);
            NotesVM = new NotesViewModel(repo, project);
            MilestonesVM = new MilestonesViewModel(repo, project);
            BlockersVM = new BlockersViewModel(repo, project);
            MembersVM = new MembersViewModel(repo, project);
            ReportsVM = new ProjectReportsTabViewModel(repo, project, TasksVM, BlockersVM);

            _ = LoadDataAsync();
        }

        [RelayCommand]
        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                await Task.WhenAll(
                    TasksVM.LoadAsync(),
                    NotesVM.LoadAsync(),
                    MilestonesVM.LoadAsync(),
                    BlockersVM.LoadAsync(),
                    MembersVM.LoadAsync(),
                    ReportsVM.LoadAsync()
                );
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "ProjectDetailLoad");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public partial class TasksViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;

        [ObservableProperty] private ObservableCollection<TaskItem> _tasks = new();
        [ObservableProperty] private string _newTaskTitle = string.Empty;

        public TasksViewModel(ISupabaseRepository repo, Project project)
        {
            _repo = repo;
            _project = project;
        }

        public async Task LoadAsync()
        {
            var myTasks = (await _repo.GetTasksAsync(_project.Id))
                .OrderBy(task => task.DueDate ?? DateTime.MaxValue)
                .ThenBy(task => task.CreatedAt)
                .ToList();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Tasks.Clear();
                foreach (var task in myTasks)
                {
                    Tasks.Add(task);
                }
            });
        }

        [RelayCommand]
        private async Task AddTaskAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle))
            {
                return;
            }

            var task = new TaskItem
            {
                Title = NewTaskTitle.Trim(),
                ProjectId = _project.Id
            };

            var created = await _repo.CreateTaskAsync(task);
            if (created != null)
            {
                Tasks.Add(created);
            }

            NewTaskTitle = string.Empty;
        }

        [RelayCommand]
        private async Task DeleteTaskAsync(TaskItem? task)
        {
            if (task == null)
            {
                return;
            }

            await _repo.DeleteTaskAsync(task.Id);
            Tasks.Remove(task);
        }

        [RelayCommand]
        private async Task ToggleTaskCompletedAsync(TaskItem? task)
        {
            if (task == null)
            {
                return;
            }

            task.IsCompleted = !task.IsCompleted;
            await _repo.UpdateTaskAsync(task);
        }
    }

    public partial class NotesViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;

        [ObservableProperty] private ObservableCollection<ProjectNote> _notes = new();
        [ObservableProperty] private string _newNoteContent = string.Empty;

        public NotesViewModel(ISupabaseRepository repo, Project project)
        {
            _repo = repo;
            _project = project;
        }

        public async Task LoadAsync()
        {
            var myNotes = (await _repo.GetProjectNotesAsync(_project.Id))
                .OrderByDescending(note => note.CreatedAt)
                .ToList();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Notes.Clear();
                foreach (var note in myNotes)
                {
                    Notes.Add(note);
                }
            });
        }

        [RelayCommand]
        private async Task AddNoteAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNoteContent))
            {
                return;
            }

            var note = new ProjectNote
            {
                Content = NewNoteContent.Trim(),
                ProjectId = _project.Id
            };

            var created = await _repo.CreateProjectNoteAsync(note);
            if (created != null)
            {
                Notes.Insert(0, created);
            }

            NewNoteContent = string.Empty;
        }

        [RelayCommand]
        private async Task DeleteNoteAsync(ProjectNote? note)
        {
            if (note == null)
            {
                return;
            }

            await _repo.DeleteProjectNoteAsync(note.Id);
            Notes.Remove(note);
        }
    }

    public partial class MilestonesViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;

        [ObservableProperty] private ObservableCollection<Milestone> _milestones = new();

        public MilestonesViewModel(ISupabaseRepository repo, Project project)
        {
            _repo = repo;
            _project = project;
        }

        public async Task LoadAsync()
        {
            var milestones = (await _repo.GetMilestonesAsync(_project.Id))
                .OrderBy(milestone => milestone.DueDate ?? DateTime.MaxValue)
                .ToList();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Milestones.Clear();
                foreach (var milestone in milestones)
                {
                    Milestones.Add(milestone);
                }
            });
        }

        [RelayCommand]
        private async Task AddMilestoneAsync()
        {
            var milestone = new Milestone
            {
                Title = "New Milestone",
                ProjectId = _project.Id,
                DueDate = DateTime.UtcNow.AddDays(7)
            };

            var created = await _repo.CreateMilestoneAsync(milestone);
            if (created != null)
            {
                Milestones.Add(created);
            }
        }

        [RelayCommand]
        private async Task DeleteMilestoneAsync(Milestone? milestone)
        {
            if (milestone == null)
            {
                return;
            }

            await _repo.DeleteMilestoneAsync(milestone.Id);
            Milestones.Remove(milestone);
        }
    }

    public partial class BlockersViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;

        [ObservableProperty] private ObservableCollection<ProjectBlocker> _blockers = new();
        [ObservableProperty] private string _newBlockerDesc = string.Empty;

        public BlockersViewModel(ISupabaseRepository repo, Project project)
        {
            _repo = repo;
            _project = project;
        }

        public async Task LoadAsync()
        {
            var blockers = (await _repo.GetProjectBlockersAsync(_project.Id))
                .OrderByDescending(blocker => blocker.CreatedAt)
                .ToList();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Blockers.Clear();
                foreach (var blocker in blockers)
                {
                    Blockers.Add(blocker);
                }
            });
        }

        [RelayCommand]
        private async Task AddBlockerAsync()
        {
            if (string.IsNullOrWhiteSpace(NewBlockerDesc))
            {
                return;
            }

            var blocker = new ProjectBlocker
            {
                Description = NewBlockerDesc.Trim(),
                ProjectId = _project.Id
            };

            var created = await _repo.CreateProjectBlockerAsync(blocker);
            if (created != null)
            {
                Blockers.Insert(0, created);
            }

            NewBlockerDesc = string.Empty;
        }

        [RelayCommand]
        private async Task DeleteBlockerAsync(ProjectBlocker? blocker)
        {
            if (blocker == null)
            {
                return;
            }

            await _repo.DeleteProjectBlockerAsync(blocker.Id);
            Blockers.Remove(blocker);
        }

        [RelayCommand]
        private async Task ResolveBlockerAsync(ProjectBlocker? blocker)
        {
            if (blocker == null)
            {
                return;
            }

            blocker.Resolved = !blocker.Resolved;
            await _repo.UpdateProjectBlockerAsync(blocker);
        }
    }

    public partial class MembersViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;

        [ObservableProperty] private ObservableCollection<ProjectMember> _members = new();

        public MembersViewModel(ISupabaseRepository repo, Project project)
        {
            _repo = repo;
            _project = project;
        }

        public async Task LoadAsync()
        {
            var members = (await _repo.GetProjectMembersAsync(_project.Id))
                .OrderBy(member => member.CreatedAt)
                .ToList();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Members.Clear();
                foreach (var member in members)
                {
                    Members.Add(member);
                }
            });
        }

        [RelayCommand]
        private async Task AddProjectMemberAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var member = new ProjectMember
            {
                UserId = userId,
                ProjectId = _project.Id,
                Role = "Member"
            };

            var created = await _repo.CreateProjectMemberAsync(member);
            if (created != null)
            {
                Members.Insert(0, created);
            }
        }

        [RelayCommand]
        private async Task RemoveProjectMemberAsync(ProjectMember? member)
        {
            if (member == null)
            {
                return;
            }

            await _repo.DeleteProjectMemberAsync(member.Id);
            Members.Remove(member);
        }
    }

    public partial class ProjectReportsTabViewModel : ObservableObject
    {
        private readonly ISupabaseRepository _repo;
        private readonly Project _project;
        private readonly TasksViewModel _tasksVm;
        private readonly BlockersViewModel _blockersVm;

        [ObservableProperty] private ObservableCollection<ProjectReport> _reports = new();

        public ProjectReportsTabViewModel(ISupabaseRepository repo, Project project, TasksViewModel tasksVm, BlockersViewModel blockersVm)
        {
            _repo = repo;
            _project = project;
            _tasksVm = tasksVm;
            _blockersVm = blockersVm;
        }

        public async Task LoadAsync()
        {
            var reports = await _repo.GetReportsAsync();
            var projectReports = reports
                .Where(report => report.ProjectName == _project.Name)
                .OrderByDescending(report => report.ReportDate)
                .ToList();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                Reports.Clear();
                foreach (var report in projectReports)
                {
                    Reports.Add(report);
                }
            });
        }

        [RelayCommand]
        private async Task GenerateDailyReportAsync()
        {
            var todayTasks = _tasksVm.Tasks.Where(t => t.IsCompleted && t.CreatedAt >= DateTime.UtcNow.AddDays(-1)).ToList();
            var pendingTasks = _tasksVm.Tasks.Where(t => !t.IsCompleted).ToList();
            var activeBlockers = _blockersVm.Blockers.Where(b => !b.Resolved).ToList();

            var report = new ProjectReport
            {
                ProjectName = _project.Name,
                CompletedTasks = todayTasks.Select(t => t.Title).ToList(),
                NextSteps = pendingTasks.Select(t => t.Title).ToList(),
                Blockers = activeBlockers.Select(b => b.Description).ToList()
            };

            await _repo.CreateReportAsync(report);
            Reports.Insert(0, report);
        }

        [RelayCommand]
        private Task ExportProjectReportAsync()
        {
            MessageBox.Show($"Exported Project {_project.Name} Report successfully. (Mock)", "Export Complete");
            return Task.CompletedTask;
        }
    }
}
