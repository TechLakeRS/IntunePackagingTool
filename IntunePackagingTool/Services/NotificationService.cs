using IntunePackagingTool.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IntunePackagingTool.Services
{
    public class NotificationService
    {
        private static NotificationService? _instance;
        private Panel? _toastContainer;
        private readonly List<ToastNotification> _activeToasts = new();
        private readonly List<NotificationHistoryItem> _history = new();
        private const int MAX_VISIBLE_TOASTS = 3;
        private const int TOAST_SPACING = 10;

        public static NotificationService Instance => _instance ??= new NotificationService();

        public event EventHandler<NotificationHistoryItem>? NotificationAdded;
        public event EventHandler<string>? NotificationClicked;

        public IReadOnlyList<NotificationHistoryItem> History => _history.AsReadOnly();

        private NotificationService()
        {
        }

        public void Initialize(Panel toastContainer)
        {
            _toastContainer = toastContainer;
        }

        public string ShowSuccess(string title, string message = "", int autoCloseDuration = 5000)
        {
            return ShowToast(ToastType.Success, title, message, autoCloseDuration);
        }

        public string ShowError(string title, string message = "", int autoCloseDuration = 0) // Errors don't auto-close
        {
            return ShowToast(ToastType.Error, title, message, autoCloseDuration);
        }

        public string ShowWarning(string title, string message = "", int autoCloseDuration = 5000)
        {
            return ShowToast(ToastType.Warning, title, message, autoCloseDuration);
        }

        public string ShowInfo(string title, string message = "", int autoCloseDuration = 3000)
        {
            return ShowToast(ToastType.Info, title, message, autoCloseDuration);
        }

        public string ShowProgress(string title, string message = "", int progress = -1)
        {
            return ShowToast(ToastType.InProgress, title, message, 0, progress);
        }

        public void UpdateProgress(string notificationId, int progress, string? message = null)
        {
            var toast = _activeToasts.FirstOrDefault(t => t.NotificationId == notificationId);
            toast?.UpdateProgress(progress, message);

            // Update history
            var historyItem = _history.FirstOrDefault(h => h.Id == notificationId);
            if (historyItem != null)
            {
                historyItem.Progress = progress;
                if (!string.IsNullOrEmpty(message))
                {
                    historyItem.Message = message;
                }
            }
        }

        public void CompleteProgress(string notificationId, bool success, string? finalMessage = null)
        {
            var toast = _activeToasts.FirstOrDefault(t => t.NotificationId == notificationId);
            if (toast != null)
            {
                // Remove the progress toast
                RemoveToast(toast);

                // Show completion toast
                if (success)
                {
                    ShowSuccess(toast.Title, finalMessage ?? "Completed successfully", 5000);
                }
                else
                {
                    ShowError(toast.Title, finalMessage ?? "Operation failed");
                }
            }

            // Update history
            var historyItem = _history.FirstOrDefault(h => h.Id == notificationId);
            if (historyItem != null)
            {
                historyItem.Type = success ? ToastType.Success : ToastType.Error;
                historyItem.CompletedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(finalMessage))
                {
                    historyItem.Message = finalMessage;
                }
            }
        }

        private string ShowToast(ToastType type, string title, string message, int autoCloseDuration, int progress = -1)
        {
            if (_toastContainer == null)
            {
                throw new InvalidOperationException("NotificationService not initialized. Call Initialize() first.");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new ToastNotification();
                var notificationId = toast.NotificationId;

                // Add to history
                var historyItem = new NotificationHistoryItem
                {
                    Id = notificationId,
                    Type = type,
                    Title = title,
                    Message = message,
                    Timestamp = DateTime.Now,
                    Progress = progress
                };
                _history.Insert(0, historyItem); // Add to beginning
                NotificationAdded?.Invoke(this, historyItem);

                // Handle events
                toast.Clicked += (s, e) =>
                {
                    NotificationClicked?.Invoke(this, notificationId);
                };

                toast.Closed += (s, e) =>
                {
                    RemoveToast(toast);
                };

                // Show toast
                toast.Show(type, title, message, autoCloseDuration, progress);

                // Add to container
                _toastContainer.Children.Add(toast);
                _activeToasts.Add(toast);

                // Position toasts
                RepositionToasts();

                // Remove oldest if we have too many
                while (_activeToasts.Count > MAX_VISIBLE_TOASTS)
                {
                    var oldestToast = _activeToasts.First();
                    oldestToast.Close();
                }
            });

            return _activeToasts.Last().NotificationId;
        }

        private void RemoveToast(ToastNotification toast)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _activeToasts.Remove(toast);
                _toastContainer?.Children.Remove(toast);
                RepositionToasts();
            });
        }

        private void RepositionToasts()
        {
            double yOffset = 0;
            for (int i = _activeToasts.Count - 1; i >= 0; i--)
            {
                var toast = _activeToasts[i];
                Canvas.SetBottom(toast, yOffset);
                Canvas.SetRight(toast, 20);

                yOffset += toast.ActualHeight > 0 ? toast.ActualHeight + TOAST_SPACING : 80 + TOAST_SPACING;
            }
        }

        public void ClearHistory()
        {
            _history.Clear();
        }
    }

    public class NotificationHistoryItem
    {
        public string Id { get; set; } = "";
        public ToastType Type { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int Progress { get; set; } = -1;

        public string TimeAgo
        {
            get
            {
                var timeSpan = DateTime.Now - Timestamp;
                if (timeSpan.TotalMinutes < 1) return "Just now";
                if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} min ago";
                if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hour(s) ago";
                return $"{(int)timeSpan.TotalDays} day(s) ago";
            }
        }

        public string Duration
        {
            get
            {
                if (CompletedAt.HasValue)
                {
                    var duration = CompletedAt.Value - Timestamp;
                    if (duration.TotalSeconds < 60) return $"{(int)duration.TotalSeconds}s";
                    if (duration.TotalMinutes < 60) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
                    return $"{(int)duration.TotalHours}h {duration.Minutes}m";
                }
                return Type == ToastType.InProgress ? "In progress..." : "";
            }
        }
    }
}
