using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IntunePackagingTool.Dialogs
{
    public partial class AddDeviceDialog : Window
    {
        private readonly IntuneService _intuneService;
        private readonly string _groupId;
        private readonly string _groupName;

        public ObservableCollection<DeviceAddResult> DeviceResults { get; set; }
        public int SuccessfullyAddedCount { get; private set; }

        public AddDeviceDialog(IntuneService intuneService, string groupId, string groupName)
        {
            InitializeComponent();

            _intuneService = intuneService;
            _groupId = groupId;
            _groupName = groupName;

            DeviceResults = new ObservableCollection<DeviceAddResult>();
            DataContext = this;

            // Set group info
            GroupNameText.Text = _groupName;

            // Focus on input field
            Loaded += (s, e) => DeviceNameInput.Focus();
        }

        private async void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            await AddDeviceByName();
        }

        private async void DeviceNameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AddDeviceByName();
            }
        }

        private async Task AddDeviceByName()
        {
            var deviceName = DeviceNameInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                ShowInlineMessage("Please enter a device name", MessageType.Warning);
                return;
            }

            // Clear previous messages
            InlineMessageBorder.Visibility = Visibility.Collapsed;

            // Disable input during processing
            SetInputEnabled(false);
            AddingProgressBar.Visibility = Visibility.Visible;

            try
            {
                // Check if device exists in Intune
                var device = await _intuneService.FindDeviceByNameAsync(deviceName);

                if (device == null)
                {
                    // Device not found
                    var result = new DeviceAddResult
                    {
                        DeviceName = deviceName,
                        Status = AddStatus.NotFound,
                        Message = "Device not found in Intune",
                        Timestamp = DateTime.Now
                    };

                    DeviceResults.Insert(0, result);
                    ShowInlineMessage($"Device '{deviceName}' not found in Intune", MessageType.Error);
                }
                else
                {
                    // Check if device is already in the group
                    var isAlreadyMember = await _intuneService.IsDeviceInGroupAsync(device.Id, _groupId);

                    if (isAlreadyMember)
                    {
                        var result = new DeviceAddResult
                        {
                            DeviceName = device.DeviceName,
                            UserPrincipalName = device.UserPrincipalName,
                            Status = AddStatus.AlreadyExists,
                            Message = "Already in group",
                            Timestamp = DateTime.Now
                        };

                        DeviceResults.Insert(0, result);
                        ShowInlineMessage($"Device '{device.DeviceName}' is already in this group", MessageType.Info);
                    }
                    else
                    {
                        // Add device to group
                        await _intuneService.AddDeviceToGroupAsync(device.Id, _groupId);

                        var result = new DeviceAddResult
                        {
                            DeviceName = device.DeviceName,
                            UserPrincipalName = device.UserPrincipalName,
                            Status = AddStatus.Success,
                            Message = "Successfully added",
                            Timestamp = DateTime.Now
                        };

                        DeviceResults.Insert(0, result);
                        SuccessfullyAddedCount++;

                        ShowInlineMessage($"Successfully added '{device.DeviceName}' to the group", MessageType.Success);
                        UpdateSummary();
                    }
                }

                // Clear input for next entry
                DeviceNameInput.Clear();
                DeviceNameInput.Focus();

                // Show results panel if hidden
                if (ResultsPanel.Visibility == Visibility.Collapsed)
                {
                    ResultsPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                var result = new DeviceAddResult
                {
                    DeviceName = deviceName,
                    Status = AddStatus.Error,
                    Message = $"Error: {ex.Message}",
                    Timestamp = DateTime.Now
                };

                DeviceResults.Insert(0, result);
                ShowInlineMessage($"Error adding device: {ex.Message}", MessageType.Error);
            }
            finally
            {
                SetInputEnabled(true);
                AddingProgressBar.Visibility = Visibility.Collapsed;
            }
        }


        private void ShowInlineMessage(string message, MessageType type)
        {
            InlineMessageText.Text = message;

            // Set icon and color based on type
            switch (type)
            {
                case MessageType.Success:
                    InlineMessageIcon.Text = "✓";
                    InlineMessageBorder.Background = System.Windows.Media.Brushes.LightGreen;
                    break;
                case MessageType.Warning:
                    InlineMessageIcon.Text = "⚠";
                    InlineMessageBorder.Background = System.Windows.Media.Brushes.LightYellow;
                    break;
                case MessageType.Error:
                    InlineMessageIcon.Text = "✗";
                    InlineMessageBorder.Background = System.Windows.Media.Brushes.MistyRose;
                    break;
                case MessageType.Info:
                    InlineMessageIcon.Text = "ℹ";
                    InlineMessageBorder.Background = System.Windows.Media.Brushes.LightBlue;
                    break;
            }

            InlineMessageBorder.Visibility = Visibility.Visible;
        }

        private void SetInputEnabled(bool enabled)
        {
            DeviceNameInput.IsEnabled = enabled;
            AddDeviceButton.IsEnabled = enabled;
          
        }

        private void UpdateSummary()
        {
            if (SuccessfullyAddedCount > 0)
            {
                SummaryText.Text = $"{SuccessfullyAddedCount} device(s) successfully added to the group";
                SummaryPanel.Visibility = Visibility.Visible;
            }
        }

        private void ClearResultsButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceResults.Clear();
            ResultsPanel.Visibility = Visibility.Collapsed;
            SummaryPanel.Visibility = Visibility.Collapsed;
            InlineMessageBorder.Visibility = Visibility.Collapsed;
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = SuccessfullyAddedCount > 0;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class DeviceAddResult : INotifyPropertyChanged
    {
        private string _deviceName;
        private string _userPrincipalName;
        private AddStatus _status;
        private string _message;
        private DateTime _timestamp;

        public string DeviceName
        {
            get => _deviceName;
            set { _deviceName = value; OnPropertyChanged(); }
        }

        public string UserPrincipalName
        {
            get => _userPrincipalName;
            set { _userPrincipalName = value; OnPropertyChanged(); }
        }

        public AddStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum AddStatus
    {
        Success,
        NotFound,
        AlreadyExists,
        Error
    }

    public enum MessageType
    {
        Success,
        Warning,
        Error,
        Info
    }
}