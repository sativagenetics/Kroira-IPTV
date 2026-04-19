using System;
using System.Collections.Generic;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isExportPickerOpen;

        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SettingsViewModel>();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadSettingsCommand.ExecuteAsync(null);
        }

        private async void OnExportBackupClick(object sender, RoutedEventArgs e)
        {
            if (_isExportPickerOpen)
            {
                LogExport("picker launch ignored because export picker is already open");
                return;
            }

            _isExportPickerOpen = true;
            LogExport($"command entry hasThreadAccess={DispatcherQueue.HasThreadAccess}");

            try
            {
                var picker = CreateSavePicker();
                LogExport("picker created");
                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    LogExport("picker cancelled");
                    return;
                }

                LogExport($"picker completed path='{file.Path}'");
                await ViewModel.ExportBackupAsync(file.Path);
                LogExport("viewmodel export completed");
            }
            catch (Exception ex)
            {
                LogExport($"export click failed type={ex.GetType().Name} message='{ex.Message}'");
                ViewModel.BackupStatusText = $"Backup export failed: {ex.Message}";
            }
            finally
            {
                _isExportPickerOpen = false;
                LogExport("picker state cleared");
            }
        }

        private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Restore local backup?",
                Content = "Restore replaces the current local sources, profiles, favorites, watch state, and local preferences before re-importing restored sources.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var confirmation = await dialog.ShowAsync();
            if (confirmation != ContentDialogResult.Primary)
            {
                return;
            }

            var picker = CreateOpenPicker();
            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            await ViewModel.RestoreBackupAsync(file.Path);
        }

        private Windows.Storage.Pickers.FileSavePicker CreateSavePicker()
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = $"kroira-backup-{DateTime.Now:yyyyMMdd-HHmm}"
            };

            picker.FileTypeChoices.Add("Kroira Backup", new List<string> { ".json" });
            InitializePicker(picker);
            return picker;
        }

        private Windows.Storage.Pickers.FileOpenPicker CreateOpenPicker()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".kroira-backup.json");
            InitializePicker(picker);
            return picker;
        }

        private static void InitializePicker(object picker)
        {
            var window = ((App)Application.Current).MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        private static void LogExport(string message)
        {
            BackupRuntimeLogger.Log("SETTINGS PAGE", message);
        }
    }
}
