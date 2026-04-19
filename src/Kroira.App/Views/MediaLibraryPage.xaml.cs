using System;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class StringToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class MediaLibraryPage : Page
    {
        public MediaLibraryViewModel ViewModel { get; }

        public MediaLibraryPage()
        {
            InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MediaLibraryViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.Dispose();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadAsync();
        }

        private void PlayRecording_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryRecordingItem item } && item.CanPlay)
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.PlaybackContentId,
                    ContentType = item.PlaybackContentType,
                    StreamUrl = item.OutputPath,
                    StartPositionMs = 0
                });
            }
        }

        private void PlayDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryDownloadItem item } && item.CanPlay)
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.PlaybackContentId,
                    ContentType = item.PlaybackContentType,
                    StreamUrl = item.OutputPath,
                    StartPositionMs = 0
                });
            }
        }

        private async void RetryRecording_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryRecordingItem item })
            {
                await ViewModel.RetryRecordingAsync(item.Id);
            }
        }

        private async void RetryDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryDownloadItem item })
            {
                await ViewModel.RetryDownloadAsync(item.Id);
            }
        }

        private async void CancelRecording_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryRecordingItem item })
            {
                await ViewModel.CancelRecordingAsync(item.Id);
            }
        }

        private async void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryDownloadItem item })
            {
                await ViewModel.CancelDownloadAsync(item.Id);
            }
        }

        private async void DeleteRecording_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryRecordingItem item })
            {
                await ViewModel.DeleteRecordingAsync(item.Id);
            }
        }

        private async void DeleteDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MediaLibraryDownloadItem item })
            {
                await ViewModel.DeleteDownloadAsync(item.Id);
            }
        }
    }
}
