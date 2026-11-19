using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using System.Text.Json;

namespace NowPlayingWidget
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _hideTimer;

        // Session managing
        private readonly object _sessionsLock = new();
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private readonly List<GlobalSystemMediaTransportControlsSession> _allSessions = new();

        //Icons
        //Svg codes for both play and pause icons
        private readonly Geometry _playIcon = Geometry.Parse("M 0,1 L 9,6 L 0,11 Z");
        private readonly Geometry _pauseIcon = Geometry.Parse("M 0,1 H 3 V 11 H 0 Z M 6,1 H 9 V 11 H 6 Z");
        private Forms.NotifyIcon? _trayIcon;

        private const int SW_RESTORE = 9;
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        //File to save position of the widget
        private static readonly string PositionFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NowPlayingWidget",
        "position.json");

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        //Check if the window is minimized
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowPosition();
            SetupTrayIcon();

            // Set up the timer to appear and hide the widget after 5 seconds
            // on music start / change
            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(5);
            _hideTimer.Tick += HideTimer_Tick;

            _ = StartListeningToMusic();
        }

        private void LoadWindowPosition()
        {
            try
            {
                if (!File.Exists(PositionFilePath)) return;

                string json = File.ReadAllText(PositionFilePath);
                var pos = JsonSerializer.Deserialize<double[]>(json);
                if (pos is not { Length: 2 } || double.IsNaN(pos[0]) || double.IsNaN(pos[1])) return;

                // Verify the saved coordinates are still visible on the current monitor setup
                double minX = SystemParameters.VirtualScreenLeft;
                double minY = SystemParameters.VirtualScreenTop;
                double maxX = minX + SystemParameters.VirtualScreenWidth - Width;
                double maxY = minY + SystemParameters.VirtualScreenHeight - Height;

                if (pos[0] >= minX && pos[0] <= maxX && pos[1] >= minY && pos[1] <= maxY)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = pos[0];
                    Top = pos[1];
                }
            }
            catch
            {
                // Fall back to default startup position if the file is unreadable
            }
        }

        private void SaveWindowPosition()
        {
            try
            {
                if (double.IsNaN(Left) || double.IsNaN(Top)) return;

                string? dir = Path.GetDirectoryName(PositionFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(new[] { Left, Top });
                File.WriteAllText(PositionFilePath, json);
            }
            catch
            {
                // Ignore disk errors on shutdown
            }
        }

        private Drawing.Icon CreateVinylIcon()
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Outer dark vinyl disc
            using var discBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 24, 24, 24));
            g.FillEllipse(discBrush, 1, 1, 30, 30);

            // Subtle groove ring
            using var groovePen = new Drawing.Pen(Drawing.Color.FromArgb(110, 255, 255, 255), 1.2f);
            g.DrawEllipse(groovePen, 5, 5, 22, 22);

            // Accent center label (soft sky/violet tint)
            using var labelBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 130, 170, 255));
            g.FillEllipse(labelBrush, 11, 11, 10, 10);

            // Center spindle hole
            using var holeBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 20, 20, 20));
            g.FillEllipse(holeBrush, 14.5f, 14.5f, 3, 3);

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = Drawing.Icon.FromHandle(hIcon);
                return (Drawing.Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private void SetupTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show Widget", null, (_, _) => ShowWidget());
            menu.Items.Add("Exit", null, (_, _) => Close());

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = CreateVinylIcon(),
                Text = "Now Playing Widget",
                ContextMenuStrip = menu,
                Visible = true
            };

            // Clicking the tray icon wakes up the widget
            _trayIcon.Click += (_, e) =>
            {
                if (e is Forms.MouseEventArgs me && me.Button == Forms.MouseButtons.Left)
                {
                    ShowWidget();
                }
            };
        }

        // Allows dragging the widget when it is visible or double clicking the widget / clicking the art to open the app playing the media
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ClickCount == 2)
            {
                FocusCurrentApp();
                return;
            }
            // Record window position before dragging
            double startLeft = Left;
            double startTop = Top;

            DragMove();

            // If the widget barely moved and you clicked the album art, treat it as a click and open the app playing the media
            bool didMove = Math.Abs(Left - startLeft) > 2 || Math.Abs(Top - startTop) > 2;
            if (didMove)
            {
                SaveWindowPosition();
            }
            else if (e.OriginalSource == AlbumArt)
            {
                FocusCurrentApp();
            }
        }

        // Captures any click on the widget (drag, button press, etc.) to keep it visible
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            ShowWidget();
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            // Keep the widget visible while scrolling
            ShowWidget();

            GlobalSystemMediaTransportControlsSession? targetSession = null;

            lock (_sessionsLock)
            {
                if (_allSessions.Count <= 1) return;

                int currentIndex = _currentSession != null ? _allSessions.IndexOf(_currentSession) : -1;
                int direction = e.Delta < 0 ? 1 : -1;
                int nextIndex = (currentIndex + direction + _allSessions.Count) % _allSessions.Count;
                targetSession = _allSessions[nextIndex];
            }

            if (targetSession != null)
            {
                UpdateSong(targetSession);
            }
        }

        private void HideTimer_Tick(object? sender, EventArgs e)
        {
            _hideTimer.Stop();

            // Smooth 1-second fade out animation
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = Opacity,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(1))
            };
            BeginAnimation(Window.OpacityProperty, fadeOut);
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

        //Open the app playing the current media. If its a browser, open the proper window if possible.
        private void FocusCurrentApp()
        {
            if (_currentSession == null) return;

            string appId = _currentSession.SourceAppUserModelId?.ToLowerInvariant() ?? string.Empty;

            string? processName = appId switch
            {
                var s when s.Contains("spotify") => "Spotify",
                var s when s.Contains("chrome") => "chrome",
                var s when s.Contains("msedge") => "msedge",
                var s when s.Contains("firefox") => "firefox",
                var s when s.Contains("brave") => "brave",
                var s when s.Contains("opera") => "opera",
                _ => null
            };

            if (processName == null) return;

            // Collect all process IDs belonging to that app
            var processIds = new HashSet<uint>();
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                using (proc)
                {
                    processIds.Add((uint)proc.Id);
                }
            }

            if (processIds.Count == 0) return;

            string currentTitle = TitleText.Text;
            IntPtr exactMatch = IntPtr.Zero;
            IntPtr fallbackWindow = IntPtr.Zero;

            // Scan every visible window belonging to those processes
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!processIds.Contains(pid)) return true;

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string windowTitle = sb.ToString();

                if (string.IsNullOrWhiteSpace(windowTitle)) return true;

                // Remember the first valid window as a fallback
                if (fallbackWindow == IntPtr.Zero)
                {
                    fallbackWindow = hWnd;
                }

                // If this window's title matches the song/video title, lock onto it
                if (!string.IsNullOrEmpty(currentTitle) &&
                    windowTitle.Contains(currentTitle, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatch = hWnd;
                    return false; // Stop searching
                }

                return true;
            }, IntPtr.Zero);

            IntPtr target = exactMatch != IntPtr.Zero ? exactMatch : fallbackWindow;
            if (target != IntPtr.Zero)
            {
                if (IsIconic(target))
                {
                    ShowWindow(target, SW_RESTORE);
                }

                SetForegroundWindow(target);
            }
        }

        //Ask Windows if some media started playing and get the manager
        private async Task StartListeningToMusic()
        {
            try
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
            catch
            {
                // Ignore transient WinRT startup errors if media service isn't ready
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

            lock (_sessionsLock)
            {
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
            if (newSession == _currentSession) return;

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
                UpdatePlayPauseIcon(_currentSession);
            }
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
            if (sender == _currentSession)
            {
                await RefreshUIAsync(sender);
            }
        }

        private async Task RefreshUIAsync(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                if (properties == null) return;

                BitmapImage? albumArtImage = null;

                if (properties.Thumbnail != null)
                {
                    using var winrtStream = await properties.Thumbnail.OpenReadAsync();
                    using var netStream = winrtStream.AsStream();

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 112;
                    bitmap.StreamSource = netStream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    albumArtImage = bitmap;
                }

                Dispatcher.Invoke(() =>
                {
                    TitleText.Text = properties.Title;
                    ArtistText.Text = properties.Artist;
                    AlbumArt.Source = albumArtImage;
                    ShowWidget();
                });
            }
            catch
            {
                // Ignore transient errors if the media session closes or provides invalid thumbnail bytes
            }
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipPreviousAsync();
                }
            }
            catch { }
        }

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TryTogglePlayPauseAsync();
                }
            }
            catch { }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipNextAsync();
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hideTimer.Stop();
            if (_sessionManager != null)
            {
                _sessionManager.SessionsChanged -= SessionManager_SessionsChanged;
                _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;
            }

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            }

            lock (_sessionsLock)
            {
                foreach (var session in _allSessions)
                {
                    session.PlaybackInfoChanged -= AnySession_PlaybackInfoChanged;
                }
                _allSessions.Clear();
            }

            if (_trayIcon != null)
            {
                _trayIcon.ContextMenuStrip?.Dispose();
                _trayIcon.Icon?.Dispose();
                _trayIcon.Dispose();
            }

            base.OnClosed(e);
        }
    }
}