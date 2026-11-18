using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;

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

        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private readonly List<GlobalSystemMediaTransportControlsSession> _allSessions = new();

        //Svg codes for both play and pause icons
        private readonly Geometry _playIcon = Geometry.Parse("M 0,1 L 9,6 L 0,11 Z");
        private readonly Geometry _pauseIcon = Geometry.Parse("M 0,1 H 3 V 11 H 0 Z M 6,1 H 9 V 11 H 6 Z");

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

        // Captures any click on the widget (drag, button press, etc.) to keep it visible
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);

            // Reset the countdown timer and keep it visible
            ShowWidget();
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
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                SyncSessions();
                UpdateSong(_sessionManager.GetCurrentSession());
            }
        }

        private void SessionManager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            SyncSessions();
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateSong(sender.GetCurrentSession());
        }

        // Tracks all open media apps so whichever one starts playing takes over the widget
        private void SyncSessions()
        {
            if (_sessionManager == null) return;

            foreach (var session in _allSessions)
            {
                session.PlaybackInfoChanged -= AnySession_PlaybackInfoChanged;
            }
            _allSessions.Clear();

            foreach (var session in _sessionManager.GetSessions())
            {
                session.PlaybackInfoChanged += AnySession_PlaybackInfoChanged;
                _allSessions.Add(session);
            }
        }

        private void AnySession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            var status = sender.GetPlaybackInfo()?.PlaybackStatus;

            // If a different app just started playing, switch the widget to it immediately
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && sender != _currentSession)
            {
                UpdateSong(sender);
                return;
            }

            // Otherwise, if it's the currently displayed app, just update its Play/Pause icon
            if (sender == _currentSession)
            {
                UpdatePlayPauseIcon(sender, showIfPlaying: true);
            }
        }

        private void UpdateSong(GlobalSystemMediaTransportControlsSession? newSession)
        {
            // If we are already listening to a session, unsubscribe to prevent duplicate events
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            }

            _currentSession = newSession;

            // Subscribe to the new session
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;

                _ = RefreshUIAsync(_currentSession);
                UpdatePlayPauseIcon(_currentSession);
            }
        }
        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            UpdatePlayPauseIcon(sender, showIfPlaying: true);
        }

        private void UpdatePlayPauseIcon(GlobalSystemMediaTransportControlsSession session, bool showIfPlaying = false)
        {
            var playbackInfo = session.GetPlaybackInfo();
            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            Dispatcher.Invoke(() =>
            {
                PlayPauseIcon.Data = isPlaying ? _pauseIcon : _playIcon;
                //Nudge the pause icon to keep it centered, with the optical trick to center the play icon
                PlayPauseIcon.Margin = isPlaying ? new Thickness(0) : new Thickness(2, 0, 0, 0);
                if (showIfPlaying && isPlaying)
                {
                    ShowWidget();

                }
            });
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
                TitleText.Text = properties.Title;
                ArtistText.Text = properties.Artist;

                if (albumArtImage != null)
                {
                    AlbumArt.Source = albumArtImage;
                }

                // Trigger the visibility cycle whenever a new song updates the UI
                ShowWidget();
            });
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }
    }
}