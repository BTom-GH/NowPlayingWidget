using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;

namespace NowPlayingWidget
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer _hideTimer;
        // Keep track of the current session to clean things properly
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        public MainWindow()
        {
            InitializeComponent();

            // Set up the timer to appear and hide the widget after 5 seconds
            // on music start / change
            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(5);
            _hideTimer.Tick += HideTimer_Tick;

            _ = StartListeningToMusic();
        }

        // Allows dragging the widget when it is visible
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }

        private void HideTimer_Tick(object? sender, EventArgs e)
        {
            _hideTimer.Stop();

            // Smooth 1-second fade out animation
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = this.Opacity,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(1))
            };
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private void ShowWidget()
        {
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4))
            };
            BeginAnimation(OpacityProperty, fadeIn);

            // Restart the countdown timer
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        //Ask Windows if some media started playing and get the manager
        private async Task StartListeningToMusic()
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (sessionManager != null)
            {
                sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                UpdateSong(sessionManager.GetCurrentSession());
            }
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateSong(sender.GetCurrentSession());
        }

        private void UpdateSong(GlobalSystemMediaTransportControlsSession? newSession)
        {
            // If we are already listening to a session, unsubscribe to prevent duplicate events
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            }

            _currentSession = newSession;

            // Subscribe to the new session
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                _ = RefreshUIAsync(_currentSession);
            }
        }

        private async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            // Windows fires this on a background thread, so we pass it to our Refresh method
            await RefreshUIAsync(sender);
        }

        private async Task RefreshUIAsync(GlobalSystemMediaTransportControlsSession session)
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            BitmapImage albumArtImage = null;

            if (properties.Thumbnail != null)
            {
                using (var stream = await properties.Thumbnail.OpenReadAsync())
                {
                    albumArtImage = new BitmapImage();
                    albumArtImage.BeginInit();
                    albumArtImage.CacheOption = BitmapCacheOption.OnLoad;
                    albumArtImage.StreamSource = stream.AsStream();
                    albumArtImage.EndInit();
                    albumArtImage.Freeze();
                }
            }

            Dispatcher.Invoke(() =>
            {
                SongText.Text = $"{properties.Title}\n{properties.Artist}";

                if (albumArtImage != null)
                {
                    AlbumArt.Source = albumArtImage;
                }

                // Trigger the visibility cycle whenever a new song updates the UI
                ShowWidget();
            });
        }
    }
}