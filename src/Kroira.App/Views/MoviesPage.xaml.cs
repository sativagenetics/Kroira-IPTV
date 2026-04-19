using System;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace Kroira.App.Views
{
    public sealed partial class MoviesPage : Page
    {
        public MoviesViewModel ViewModel { get; }

        public MoviesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MoviesViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadMoviesCommand.ExecuteAsync(null);
        }

        private async void FeaturedPlay_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie == null)
            {
                return;
            }

            var variant = await ChooseMovieVariantAsync(movie, "Play");
            if (variant == null || string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
            {
                return;
            }

            this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ContentId = variant.Movie.Id,
                ContentType = PlaybackContentType.Movie,
                StreamUrl = variant.Movie.StreamUrl,
                StartPositionMs = 0
            });
        }

        private async void FeaturedDownload_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie != null)
            {
                await QueueMovieDownloadAsync(movie);
            }
        }

        private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                await ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
            }
        }

        private async void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie })
            {
                var variant = await ChooseMovieVariantAsync(movie, "Play");
                if (variant != null && !string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                    {
                        ContentId = variant.Movie.Id,
                        ContentType = PlaybackContentType.Movie,
                        StreamUrl = variant.Movie.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
        }

        private async void MovieDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie } })
            {
                await QueueMovieDownloadAsync(movie);
            }
        }

        private async Task QueueMovieDownloadAsync(MovieBrowseItemViewModel movie)
        {
            var variant = await ChooseMovieVariantAsync(movie, "Download");
            if (variant == null || string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
            {
                return;
            }

            try
            {
                var mediaJobService = ((App)Application.Current).Services.GetRequiredService<IMediaJobService>();
                await mediaJobService.QueueDownloadAsync(
                    PlaybackContentType.Movie,
                    variant.Movie.Id,
                    variant.Movie.Title,
                    variant.DisplayName,
                    variant.Movie.StreamUrl);
            }
            catch (Exception ex)
            {
                await ShowContentDialogAsync(new ContentDialog
                {
                    Title = "Download failed",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    Content = ex.Message
                });
            }
        }

        private async Task<CatalogMovieVariant?> ChooseMovieVariantAsync(MovieBrowseItemViewModel movie, string actionLabel)
        {
            if (!movie.HasAlternateSources)
            {
                return movie.Variants.Count > 0 ? movie.Variants[0] : null;
            }

            var comboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var variant in movie.Variants)
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = variant.DisplayName,
                    Tag = variant
                });
            }

            comboBox.SelectedIndex = 0;

            var dialog = new ContentDialog
            {
                Title = movie.Title,
                PrimaryButtonText = actionLabel,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = actionLabel == "Play" ? "Choose a source for playback." : "Choose a source to download.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        comboBox
                    }
                }
            };

            var result = await ShowContentDialogAsync(dialog);
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as CatalogMovieVariant;
        }

        private static Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
        {
            var completion = new TaskCompletionSource<ContentDialogResult>();
            var operation = dialog.ShowAsync();
            operation.Completed = (info, status) =>
            {
                switch (status)
                {
                    case AsyncStatus.Completed:
                        completion.TrySetResult(info.GetResults());
                        break;
                    case AsyncStatus.Canceled:
                        completion.TrySetCanceled();
                        break;
                    case AsyncStatus.Error:
                        completion.TrySetException(info.ErrorCode);
                        break;
                }
            };

            return completion.Task;
        }
    }
}
