using System;
using System.Net.Http;
using System.Windows;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using AIHub.ViewModels;
using AIHub.Views;
using AIHub.Services;
using AIHub.Repositories;
using AIHub.Configuration;

namespace AIHub
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        private ILoggingService? _logger;
        private bool _isHandlingFatalUiError;

        public App()
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/aihub-log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<SupabaseConfig>(configuration.GetSection("Supabase"));
            services.Configure<AIConfig>(configuration.GetSection("AI"));
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false);
            });

            // Core services
            services.AddMemoryCache();

            // Supabase HTTP client configuration (shared across services)
            var supabaseConfig = configuration.GetSection("Supabase").Get<SupabaseConfig>();
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? supabaseConfig?.Url;
            var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? supabaseConfig?.AnonKey;

            services.AddHttpClient("Supabase", client =>
            {
                if (!string.IsNullOrEmpty(supabaseUrl))
                    client.BaseAddress = new Uri(supabaseUrl + "/rest/v1/");
                if (!string.IsNullOrEmpty(supabaseAnonKey))
                {
                    client.DefaultRequestHeaders.Add("apikey", supabaseAnonKey);
                    client.DefaultRequestHeaders.Add("Prefer", "return=representation");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
                }
            });

            // Repositories
            services.AddSingleton<IAuthService>(sp =>
                new AuthService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Supabase"),
                                sp.GetRequiredService<IOptions<SupabaseConfig>>(),
                                sp.GetRequiredService<ILogger<AuthService>>()));

            services.AddSingleton<ISupabaseRepository>(sp =>
                new SupabaseRepository(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Supabase"),
                                       sp.GetRequiredService<IOptions<SupabaseConfig>>(),
                                       sp.GetRequiredService<IAuthService>()));

            services.AddSingleton<ISupabaseSchemaInitializer>(sp =>
                new SupabaseSchemaInitializer(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Supabase"),
                                             sp.GetRequiredService<ILoggingService>(),
                                             sp.GetRequiredService<IOptions<SupabaseConfig>>()));

            // Services
            services.AddHttpClient<IUpdateService, UpdateService>();
            
            // Timeout policy for AI providers
            services.AddHttpClient<IAIService, AIService>()
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30)));
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddTransient<IBackupService, BackupService>();
            services.AddHttpClient<IHealthService, HealthService>()
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5)));

            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddTransient<IDashboardService, DashboardService>();

            // Register ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddSingleton<ProjectsViewModel>();
            services.AddTransient<ProjectWorkspaceViewModel>();
            services.AddTransient<ApiVaultViewModel>();
            services.AddTransient<ApiTesterViewModel>();
            services.AddTransient<WorkflowsViewModel>();
            services.AddTransient<ReportsViewModel>();
            services.AddTransient<MeetingsViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<IdeasLabViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ProfileViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<RegisterViewModel>();

            // Register Views
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // Global Exception Handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            Log.Error(e.Exception, "Unobserved Task Exception");
            _ = _logger?.LogErrorAsync(e.Exception, "Unobserved Task Exception");
        }

        private async void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Log.Error(e.Exception, "WPF UI Exception");
            if (_logger != null) await _logger.LogErrorAsync(e.Exception, "WPF UI Exception");

            if (_isHandlingFatalUiError)
            {
                return;
            }

            _isHandlingFatalUiError = true;
            Shutdown(-1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) 
            {
                 Log.Error(ex, "Domain Exception");
                 _ = _logger?.LogErrorAsync(ex, "Domain Exception");
            }
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            _logger = _serviceProvider.GetRequiredService<ILoggingService>();
            
            // Run startup processes asynchronously
            _ = Task.Run(async () => 
            {
                try 
                {
                    // Schema Validation
                    var schema = _serviceProvider.GetRequiredService<ISupabaseSchemaInitializer>();
                    await schema.EnsureTablesExistAsync();

                    // Check Health
                    var health = _serviceProvider.GetRequiredService<IHealthService>();
                    var warnings = await health.CheckHealthAsync();
                    
                    if (!string.IsNullOrEmpty(warnings))
                    {
                        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
                        mainVm.HealthWarning = warnings;
                    }

                    // Background Services
                    var updateSvc = _serviceProvider.GetRequiredService<IUpdateService>();
                    await updateSvc.CheckForUpdatesAsync();
                } 
                catch (Exception ex)
                {
                    _ = _logger?.LogErrorAsync(ex, "App Startup Sequence");
                }
            });

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

            // Try to restore a saved session before showing the UI so users are not forced to log in again.
            if (mainWindow.DataContext is MainViewModel mainVm)
            {
                await mainVm.InitializeAsync();
            }

            Log.Information("Showing main window.");
            MainWindow = mainWindow;
            mainWindow.Show();
            Log.Information("Main window shown. Application is running.");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
