using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using IntunePackagingTool.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace IntunePackagingTool.Dialogs
{
    public partial class GroupMembersDialog : Window
    {
        private readonly IntuneService _intuneService;
        private readonly string _groupId;
        private readonly string _groupName;
        private readonly string _assignmentType;

        private ObservableCollection<GroupDevice> _members;
       // private ObservableCollection<GroupDevice> _availableDevices;
        private ICollectionView _membersView;
      //  private ICollectionView _availableDevicesView;

        public GroupMembersDialog(IntuneService intuneService, string groupId, string groupName, string assignmentType)
        {
            InitializeComponent();

            _intuneService = intuneService;
            _groupId = groupId;
            _groupName = groupName;
            _assignmentType = assignmentType;

            _members = new ObservableCollection<GroupDevice>();
            _membersView = CollectionViewSource.GetDefaultView(_members);
            //    _availableDevices = new ObservableCollection<GroupDevice>();

            InitializeUI();
            LoadGroupMembers();
        }

        private void InitializeUI()
        {
            GroupNameText.Text = _groupName;
            GroupTypeText.Text = _assignmentType;

            // Set up data bindings
            MembersDataGrid.ItemsSource = _membersView;


            // Handle selection changes
            MembersDataGrid.SelectionChanged += (s, e) =>
            {
                RemoveSelectedButton.IsEnabled = MembersDataGrid.SelectedItems.Count > 0;
            };
        }

        private async void LoadGroupMembers()
        {
            try
            {
                ShowLoading(true);

                var members = await _intuneService.GetGroupMembersAsync(_groupId);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _members.Clear();
                    foreach (var member in members)
                    {
                        _members.Add(member);
                    }

                    MemberCountText.Text = $"{_members.Count} Members";

                    ShowLoading(false);

                    if (_members.Count == 0)
                    {
                        EmptyStatePanel.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                MessageBox.Show($"Error loading group members: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            var addDialog = new AddDeviceDialog(_intuneService, _groupId, _groupName)
            {
                Owner = this
            };

            if (addDialog.ShowDialog() == true && addDialog.SuccessfullyAddedCount > 0)
            {
                // Refresh the members list
                LoadGroupMembers();

                // Show success notification
                MessageBox.Show(
                    $"Successfully added {addDialog.SuccessfullyAddedCount} device(s) to the group!",
                    "Devices Added",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        

        private async void RemoveMember_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var device = button?.Tag as GroupDevice;

            if (device == null) return;

            var result = MessageBox.Show(
                $"Remove '{device.DeviceName}' from this group?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _intuneService.RemoveDeviceFromGroupAsync(_groupId, device.Id);
                    _members.Remove(device);
                    MemberCountText.Text = $"{_members.Count} Members";

                    if (_members.Count == 0)
                    {
                        EmptyStatePanel.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing device: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = MembersDataGrid.SelectedItems.Cast<GroupDevice>().ToList();

            if (!selected.Any()) return;

            var result = MessageBox.Show(
                $"Remove {selected.Count} device(s) from this group?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    RemoveSelectedButton.IsEnabled = false;

                    var deviceIds = selected.Select(d => d.Id).ToList();
                    await _intuneService.RemoveDevicesFromGroupAsync(_groupId, deviceIds);

                    foreach (var device in selected)
                    {
                        _members.Remove(device);
                    }

                    MemberCountText.Text = $"{_members.Count} Members";

                    if (_members.Count == 0)
                    {
                        EmptyStatePanel.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing devices: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    RemoveSelectedButton.IsEnabled = MembersDataGrid.SelectedItems.Count > 0;
                }
            }
        }

        private void SearchMembersBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_membersView != null)
            {
                _membersView.Filter = obj =>
                {
                    var device = obj as GroupDevice;
                    if (device == null) return false;

                    var searchText = SearchMembersBox.Text?.ToLower() ?? "";
                    return string.IsNullOrEmpty(searchText) ||
                           device.DeviceName.ToLower().Contains(searchText) ||
                           device.UserPrincipalName?.ToLower().Contains(searchText) == true;
                };
            }
        }       

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{_groupName}_Members_{DateTime.Now:yyyyMMdd}.csv",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(saveDialog.FileName))
                    {
                        writer.WriteLine("DeviceName,User,OperatingSystem,Compliance,LastSync");

                        foreach (var device in _members)
                        {
                            writer.WriteLine($"{device.DeviceName},{device.UserPrincipalName}," +
                                $"{device.OperatingSystem},{device.IsCompliant}," +
                                $"{device.LastSyncDateTime:yyyy-MM-dd HH:mm}");
                        }
                    }

                    MessageBox.Show("Export completed successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadGroupMembers();
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ShowLoading(bool show)
        {
            LoadingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            MembersDataGrid.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
    }
}