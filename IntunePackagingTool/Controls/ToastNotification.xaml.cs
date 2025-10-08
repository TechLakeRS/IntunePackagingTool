using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IntunePackagingTool.Controls
{
    public partial class ToastNotification : UserControl
    {
        private DispatcherTimer? _autoCloseTimer;
        private Storyboard? _slideInAnimation;
        private Storyboard? _slideOutAnimation;

        public event EventHandler? Clicked;
        public event EventHandler? Closed;

        public string NotificationId { get; set; } = Guid.NewGuid().ToString();
        public ToastType Type { get; private set; }
        public string Title { get; private set; } = "";
        public string Message { get; private set; } = "";
        public int Progress { get; private set; }

        public ToastNotification()
        {
            InitializeComponent();

            _slideInAnimation = (Storyboard)Resources["SlideInAnimation"];
            _slideOutAnimation = (Storyboard)Resources["SlideOutAnimation"];

            if (_slideOutAnimation != null)
            {
                _slideOutAnimation.Completed += SlideOutAnimation_Completed;
            }
        }

        public void Show(ToastType type, string title, string message = "", int autoCloseDuration = 0, int progress = -1)
        {
            Type = type;
            Title = title;
            Message = message;
            Progress = progress;

            TitleText.Text = title;

            if (!string.IsNullOrEmpty(message))
            {
                MessageText.Text = message;
                MessageText.Visibility = Visibility.Visible;
            }

            // Set icon and colors based on type
            switch (type)
            {
                case ToastType.Success:
                    IconText.Text = "✓";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                    IconText.Foreground = Brushes.White;
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    break;

                case ToastType.Error:
                    IconText.Text = "✕";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    IconText.Foreground = Brushes.White;
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    break;

                case ToastType.Warning:
                    IconText.Text = "⚠";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
                    IconText.Foreground = Brushes.White;
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    break;

                case ToastType.Info:
                    IconText.Text = "ℹ";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
                    IconText.Foreground = Brushes.White;
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    break;

                case ToastType.InProgress:
                    IconText.Text = "⏳";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
                    IconText.Foreground = Brushes.White;
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));

                    if (progress >= 0)
                    {
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.Value = progress;
                        ProgressBar.IsIndeterminate = false;
                    }
                    else
                    {
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = true;
                    }
                    break;
            }

            // Play slide-in animation
            _slideInAnimation?.Begin(this);

            // Auto-close timer
            if (autoCloseDuration > 0)
            {
                _autoCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(autoCloseDuration)
                };
                _autoCloseTimer.Tick += (s, e) =>
                {
                    _autoCloseTimer.Stop();
                    Close();
                };
                _autoCloseTimer.Start();
            }
        }

        public void UpdateProgress(int progress, string? message = null)
        {
            if (Type == ToastType.InProgress)
            {
                Progress = progress;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = progress;

                if (!string.IsNullOrEmpty(message))
                {
                    MessageText.Text = message;
                    MessageText.Visibility = Visibility.Visible;
                }
            }
        }

        public void Close()
        {
            _autoCloseTimer?.Stop();
            _slideOutAnimation?.Begin(this);
        }

        private void SlideOutAnimation_Completed(object? sender, EventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void Toast_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent toast click event
            Close();
        }
    }

    public enum ToastType
    {
        Success,
        Error,
        Warning,
        Info,
        InProgress
    }
}
