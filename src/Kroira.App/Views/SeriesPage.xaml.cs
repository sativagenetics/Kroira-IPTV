using System;
using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class SeasonPrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Season {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class EpisodePrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Episode {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed partial class SeriesPage : Page
    {
        public SeriesViewModel ViewModel { get; }

        public SeriesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SeriesViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadSeriesCommand.ExecuteAsync(null);
        }

        private void EpisodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Episode ep)
            {
                if (!string.IsNullOrWhiteSpace(ep.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                    {
                        ContentId = ep.Id,
                        ContentType = PlaybackContentType.Episode,
                        StreamUrl = ep.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((ListView)sender).SelectedItem = null;
        }
    }
}
