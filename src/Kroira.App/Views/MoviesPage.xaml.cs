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

            var variant = await ChooseMovieVariantAsync(movie);
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

        private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                await ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
            }
        }

        private async void GridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 &&
                e.AddedItems[0] is MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie })
            {
                var variant = await ChooseMovieVariantAsync(movie);
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
            ((GridView)sender).SelectedItem = null;
        }

        private async Task<CatalogMovieVariant?> ChooseMovieVariantAsync(MovieBrowseItemViewModel movie)
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
                PrimaryButtonText = "Play",
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
                            Text = "Choose a source for playback.",
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
