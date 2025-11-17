using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.ViewModels;
using WpfIndexer.Views;

namespace WpfIndexer
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        private UserSettingsService? _userSettingsService;
        private Serilog.ILogger? _searchLoggerInstance;

        public App()
        {
            const long maxLogSizeBytes = 1 * 1024 * 1024; // 1 MB
            const int retainedLogCountLimit = 5; // En fazla 5 yedek dosya (system.log.1, system.log.2...)

            Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);

            ServiceProvider = services.BuildServiceProvider();

            _userSettingsService = ServiceProvider.GetRequiredService<UserSettingsService>();

            UpdateLogLevel(_userSettingsService.Settings);

            try
            {
                _searchLoggerInstance = ServiceProvider.GetRequiredService<Serilog.ILogger>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Arama loglayıcı başlatılamadı.");
            }

            var themeService = ServiceProvider.GetRequiredService<ThemeService>();
            themeService.ApplyTheme();

            try
            {
                var historyService = ServiceProvider.GetRequiredService<SearchHistoryService>();
                await historyService.InitializeDatabaseAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Arama geçmişi veritabanı başlatılamadı.");
            }

            try
            {
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Uygulama başlatılırken kritik bir hata oluştu.");
                MessageBox.Show($"Başlatma hatası: {ex.Message}\n{ex.StackTrace}");
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var settingsService = ServiceProvider.GetService<IndexSettingsService>();
            settingsService?.SaveIndexes();

            _userSettingsService?.SaveSettings();

            // Tüm logların diske yazıldığından emin ol
            Log.CloseAndFlush();
            (_searchLoggerInstance as IDisposable)?.Dispose();

            base.OnExit(e);
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Microsoft ILogger<T> sistemini Serilog'a (Log.Logger) bağla
            services.AddLogging(builder =>
            {
                builder.AddSerilog(dispose: true);
            });

            const long maxLogSizeBytes = 1 * 1024 * 1024; // 1 MB
            const int retainedLogCountLimit = 5;

            // Arama logları için ayrı bir Serilog.ILogger'ı DI'a kaydet
            var searchLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "arama.log"),
                    fileSizeLimitBytes: maxLogSizeBytes,
                    rollOnFileSizeLimit: true, // YENİ
                    retainedFileCountLimit: retainedLogCountLimit,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {Message:lj}{NewLine}")
                .CreateLogger();

            services.AddSingleton<Serilog.ILogger>(searchLogger);

            services.AddSingleton<AutoUpdateService>();
            services.AddSingleton<UserSettingsService>();
            services.AddSingleton<IIndexService, LuceneIndexService>();
            services.AddSingleton<IndexSettingsService>();
            services.AddSingleton<SearchHistoryService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<CsvExportService>();

            services.AddSingleton<MainViewModel>();
            services.AddTransient<IndexManagementViewModel>();

            // SettingsViewModel hem SettingsWindow hem de ViewSettingsWindow için tekil olmalı
            services.AddSingleton<SettingsViewModel>();

            services.AddTransient<IndexCreationViewModel>();

            services.AddSingleton<MainWindow>(provider =>
            {
                var vm = provider.GetRequiredService<MainViewModel>();
                return new MainWindow { DataContext = vm };
            });
            services.AddTransient<IndexManagementWindow>(provider =>
            {
                var vm = provider.GetRequiredService<IndexManagementViewModel>();
                return new IndexManagementWindow { DataContext = vm };
            });
            services.AddTransient<SettingsWindow>(provider =>
            {
                var vm = provider.GetRequiredService<SettingsViewModel>();
                return new SettingsWindow(vm);
            });
            services.AddTransient<IndexCreationWindow>(provider =>
            {
                var vm = provider.GetRequiredService<IndexCreationViewModel>();
                return new IndexCreationWindow { ViewModel = vm };
            });

            services.AddTransient<ViewSettingsWindow>(provider =>
            {
                var vm = provider.GetRequiredService<SettingsViewModel>();
                return new ViewSettingsWindow(vm);
            });

            services.AddTransient<SelectFileTypesWindow>();

            services.AddTransient<HelpWindow>();
            services.AddTransient<AboutWindow>();
        }

        private void UpdateLogLevel(UserSettings settings)
        {
            var level = settings.EnableVerboseLogging
                ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;

            const long maxLogSizeBytes = 1 * 1024 * 1024; // 1 MB
            const int retainedLogCountLimit = 5;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .WriteTo.Debug()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "system.log"),
                    fileSizeLimitBytes: maxLogSizeBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: retainedLogCountLimit,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Log.Fatal(e.Exception, "Yakalanmayan bir UI istisnası oluştu (Uygulama Çökmesi).");
            MessageBox.Show(
                "Beklenmedik bir hata oluştu. Uygulama kapatılacak. Lütfen 'system.log' dosyasını kontrol edin.",
                "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
        }
    }
}