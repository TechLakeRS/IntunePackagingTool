using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using IntunePackagingTool.WizardSteps;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool
{
    public partial class IntuneUploadWizard : Window, IUploadProgress
    {
        // Keep all existing properties and services
        public ApplicationInfo? ApplicationInfo { get; set; }
        public string PackagePath { get; set; } = "";

        private ObservableCollection<DetectionRule> _detectionRules = new ObservableCollection<DetectionRule>();
        private IntuneService _intuneService = new IntuneService();
        private IntuneUploadService _uploadService;

        // Wizard-specific properties
        private int _currentStep = 0;
        private readonly UserControl[] _stepControls = new UserControl[3];

        // Step validation states
        private bool _step1Valid = false;
        private bool _step2Valid = false;
        private bool _step3Valid = false;

        public IntuneUploadWizard()
        {
            InitializeComponent();
            _uploadService = new IntuneUploadService(_intuneService);

            // ✅ PERFORMANCE: Only initialize first step immediately
            LoadStep(0);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            if (ApplicationInfo != null)
            {
                AppSummaryText.Text = $"{ApplicationInfo.Manufacturer} {ApplicationInfo.Name} v{ApplicationInfo.Version}";

                // Add a default file detection rule (from your original logic)
                _detectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = "%ProgramFiles%",
                    FileOrFolderName = $"{ApplicationInfo?.Name ?? "MyApp"}.exe",
                    CheckVersion = false
                });
            }

            // Pass detection rules to step controls when they're loaded
            UpdateStepData();
        }

        #region Step Management (High Performance)

        private void LoadStep(int stepIndex)
        {
            // ✅ PERFORMANCE: Lazy-load step controls only when needed
            if (_stepControls[stepIndex] == null)
            {
                _stepControls[stepIndex] = stepIndex switch
                {
                    0 => new AppDetailsStep(),
                    1 => new DetectionRulesStep(),
                    2 => new ReviewUploadStep(),
                    _ => throw new ArgumentException("Invalid step index")
                };

                // Wire up step events for data sharing
                SetupStepEvents(_stepControls[stepIndex], stepIndex);
            }

            // ✅ PERFORMANCE: Only one control in visual tree at a time
            StepContentArea.Content = _stepControls[stepIndex];
            _currentStep = stepIndex;

            UpdateStepData();
            UpdateProgressIndicators();
            UpdateNavigationButtons();
        }

        private void SetupStepEvents(UserControl stepControl, int stepIndex)
        {
            switch (stepIndex)
            {
                case 0:
                    if (stepControl is AppDetailsStep appStep)
                    {
                        appStep.ApplicationInfo = ApplicationInfo;
                        appStep.ValidationChanged += (valid) => {
                            _step1Valid = valid;
                            UpdateNavigationButtons();
                        };
                        appStep.DataChanged += UpdateApplicationDataFromStep1;
                    }
                    break;

                case 1:
                    if (stepControl is DetectionRulesStep detectionStep)
                    {
                        detectionStep.DetectionRules = _detectionRules;
                        detectionStep.ParentWindow = this; // For opening child dialogs
                        detectionStep.ValidationChanged += (valid) => {
                            _step2Valid = valid;
                            UpdateNavigationButtons();
                        };
                    }
                    break;

                case 2:
                    if (stepControl is ReviewUploadStep reviewStep)
                    {
                        reviewStep.ApplicationInfo = ApplicationInfo;
                        reviewStep.DetectionRules = _detectionRules;
                        var intuneFolder = Path.Combine(PackagePath, "Intune");
                        if (Directory.Exists(intuneFolder))
                        {
                            var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
                            reviewStep.PackagePath = intuneWinFiles.Length > 0 ? intuneWinFiles[0] : PackagePath;
                        }
                        else
                        {
                            reviewStep.PackagePath = PackagePath;
                        }

                        if (ApplicationInfo != null)
                        {
                            reviewStep.LoadFromApplicationInfo(ApplicationInfo);
                        }
                    }
                    break;
            }
        }

        private void UpdateStepData()
        {
            // Share data between steps
            if (_stepControls[0] is AppDetailsStep appStep)
            {
                appStep.ApplicationInfo = ApplicationInfo;
            }

            if (_stepControls[1] is DetectionRulesStep detectionStep)
            {
                detectionStep.DetectionRules = _detectionRules;
            }

            if (_stepControls[2] is ReviewUploadStep reviewStep)
            {
                reviewStep.ApplicationInfo = ApplicationInfo;
                reviewStep.DetectionRules = _detectionRules;
                reviewStep.PackagePath = PackagePath;
            }
        }

        #endregion

        #region Navigation

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 2 && ValidateCurrentStep())
            {
                // ✅ PERFORMANCE: Share data between steps before moving
                await ShareDataToNextStep();
                LoadStep(_currentStep + 1);
            }
            else if (_currentStep == 2)
            {
                if (_stepControls[0] is AppDetailsStep appStep &&
            _stepControls[2] is ReviewUploadStep reviewStep)
                {
                    reviewStep.UpdateSummaryData(
                        appStep.GetInstallCommand(),
                        appStep.GetUninstallCommand(),
                        appStep.GetDescription(),
                        appStep.GetInstallContext()
                    );
                }
                // ✅ PERFORMANCE: Trigger upload from final step
                await TriggerUploadFromStep3();
            }
        }

        private async Task ShareDataToNextStep()
        {
            // Share data from current step to next step
            if (_currentStep == 0 && _stepControls[1] != null)
            {
                // Share app details to detection rules step (if needed)
            }
            else if (_currentStep == 1 && _stepControls[2] != null)
            {
                // Share everything to review step
                if (_stepControls[0] is AppDetailsStep appStep &&
                    _stepControls[2] is ReviewUploadStep reviewStep)
                {
                    reviewStep.LoadFromApplicationInfo(reviewStep.ApplicationInfo);
                    reviewStep.UpdateSummaryData(
                        appStep.GetInstallCommand(),
                        appStep.GetUninstallCommand(),
                        appStep.GetDescription(),
                        appStep.GetInstallContext()
                    );
                }
            }
        }

        private async Task TriggerUploadFromStep3()
        {
            if (_stepControls[0] is AppDetailsStep appStep &&
                _stepControls[2] is ReviewUploadStep reviewStep)
            {
                // Update the review step with current values RIGHT BEFORE upload
                reviewStep.UpdateSummaryData(
                    appStep.GetInstallCommand(),
                    appStep.GetUninstallCommand(),
                    appStep.GetDescription(),
                    appStep.GetInstallContext()
                );
                reviewStep.SelectedIconPath = appStep.SelectedIconPath;
                Debug.WriteLine($"Passing install command to upload: '{appStep.GetInstallCommand()}'");
                Debug.WriteLine($"Passing icon path to upload: '{appStep.SelectedIconPath}'");


                // Debug to verify values
                Debug.WriteLine($"Passing install command to upload: '{appStep.GetInstallCommand()}'");

                // Now perform the upload with updated values
                await PerformUpload(
                    appStep.GetInstallCommand(),
                    appStep.GetUninstallCommand(),
                    appStep.GetDescription(),
                    appStep.GetInstallContext()
                );
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0)
            {
                LoadStep(_currentStep - 1);
            }
        }

        private bool ValidateCurrentStep()
        {
            return _currentStep switch
            {
                0 => _step1Valid,
                1 => _step2Valid,
                2 => _step3Valid,
                _ => false
            };
        }

        private void UpdateProgressIndicators()
        {
            // Update visual progress indicators
            UpdateStepIndicator(0, _currentStep >= 0, _currentStep > 0);
            UpdateStepIndicator(1, _currentStep >= 1, _currentStep > 1);
            UpdateStepIndicator(2, _currentStep >= 2, _currentStep > 2);

            // Update connection lines
            ConnectionLine1.Background = _currentStep >= 1 ? FindResource("SuccessBrush") as SolidColorBrush : new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
            ConnectionLine2.Background = _currentStep >= 2 ? FindResource("SuccessBrush") as SolidColorBrush : new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));

            // Update step text
            StepIndicatorText.Text = $"Step {_currentStep + 1} of 3";
            StepDescriptionText.Text = _currentStep switch
            {
                0 => "Configure application details",
                1 => "Define detection rules",
                2 => "Review and upload",
                _ => ""
            };
        }

        private void UpdateStepIndicator(int stepIndex, bool active, bool completed)
        {
            var circle = stepIndex switch
            {
                0 => Step1Circle,
                1 => Step2Circle,
                2 => Step3Circle,
                _ => null
            };

            var checkMark = stepIndex switch
            {
                0 => Step1CheckMark,
                1 => Step2CheckMark,
                2 => Step3CheckMark,
                _ => null
            };

            var icon = stepIndex switch
            {
                0 => Step1Icon,
                1 => Step2Icon,
                2 => Step3Icon,
                _ => null
            };

            if (circle == null || checkMark == null || icon == null) return;

            if (completed)
            {
                circle.Background = FindResource("SuccessBrush") as SolidColorBrush;
                checkMark.Visibility = Visibility.Visible;
                icon.Visibility = Visibility.Collapsed;
            }
            else if (active)
            {
                circle.Background = System.Windows.Media.Brushes.White;
                checkMark.Visibility = Visibility.Collapsed;
                icon.Visibility = Visibility.Visible;
                icon.Foreground = FindResource("PrimaryBrush") as SolidColorBrush;
            }
            else
            {
                circle.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255));
                checkMark.Visibility = Visibility.Collapsed;
                icon.Visibility = Visibility.Visible;
                icon.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 255, 255, 255));
            }
        }

        private void UpdateNavigationButtons()
        {
            PreviousButton.IsEnabled = _currentStep > 0;

            if (_currentStep < 2)
            {
                NextButton.Content = "Continue →";
                NextButton.Style = (Style)FindResource("WizardPrimaryButton");
                NextButton.IsEnabled = ValidateCurrentStep();
            }
            else
            {
                NextButton.Content = "🚀 Upload to Intune";
                NextButton.Style = (Style)FindResource("WizardSuccessButton");
                NextButton.IsEnabled = _step2Valid; // Must have valid detection rules
            }

            // Show validation feedback
            if (!ValidateCurrentStep() && _currentStep < 2)
            {
                ValidationStatusPanel.Visibility = Visibility.Visible;
                ValidationIcon.Text = "⚠️";
                ValidationMessage.Text = _currentStep switch
                {
                    0 => "Please complete all application details",
                    1 => "Please configure at least one detection rule",
                    _ => ""
                };
            }
            else
            {
                ValidationStatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Event Handlers from Step Controls

        private void UpdateApplicationDataFromStep1(ApplicationInfo updatedInfo)
        {
            ApplicationInfo = updatedInfo;
            AppSummaryText.Text = $"{updatedInfo.Manufacturer} {updatedInfo.Name} v{updatedInfo.Version}";
            UpdateStepData(); // Propagate to other steps
        }

        // This method will be called from DetectionRulesStep when user opens detection dialogs
       

       

        public void RemoveDetectionRule(DetectionRule rule)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to remove this detection rule?\n\n{rule.Title}",
                "Remove Detection Rule",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _detectionRules.Remove(rule);
                UpdateStepData(); // Refresh detection rules step
            }
        }

        #endregion

        #region Upload Logic (Preserved from Original)

        private async Task PerformUpload(string installCommand, string uninstallCommand, string description, string installContext)
        {
            try
            {
                if (_detectionRules.Count == 0)
                {
                    MessageBox.Show("Please add at least one detection rule.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Debug.WriteLine("=== UI DETECTION RULES ===");
                Debug.WriteLine($"Total rules from UI: {_detectionRules.Count}");
                foreach (var rule in _detectionRules)
                {
                    Debug.WriteLine($"Rule: Type={rule.Type}, Path='{rule.Path}', File='{rule.FileOrFolderName}', CheckVersion={rule.CheckVersion}");
                }
                Debug.WriteLine("=== END UI DETECTION RULES ===");

                if (string.IsNullOrEmpty(PackagePath) || ApplicationInfo == null)
                {
                    MessageBox.Show("Package information is not available.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Disable navigation during upload
                NextButton.IsEnabled = false;
                PreviousButton.IsEnabled = false;
                CancelButton.IsEnabled = false;

                if (_stepControls[2] is ReviewUploadStep reviewStep)
                {
                    // This will show progress bar and create groups
                    bool success = await reviewStep.StartUploadAsync();

                    if (success)
                    {
                        DialogResult = true;
                        Close();
                    }
                }
                else
                {
                    MessageBox.Show("Review step not loaded properly", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Upload failed: {ex.Message}\n\n" +
                    $"The .intunewin file may have been created locally, but the upload to Intune failed.\n" +
                    $"You can upload it manually through the Intune admin center.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);

                Debug.WriteLine($"Upload error: {ex}");
            }
            finally
            {
                // Re-enable navigation
                NextButton.IsEnabled = true;
                PreviousButton.IsEnabled = _currentStep > 0;
                CancelButton.IsEnabled = true;
            }
        }

        // Implement IUploadProgress interface (same as original)
        public void UpdateProgress(int percentage, string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Update the progress in ReviewUploadStep
                if (_stepControls[2] is ReviewUploadStep reviewStep)
                {
                    reviewStep.UpdateProgress(percentage, message);
                }
            });
        }

        #endregion

        #region Event Handlers

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _intuneService?.Dispose();
            _uploadService?.Dispose();
        }

        #endregion

        #region Public Methods for Step Access (Performance Optimized)

        
        public ObservableCollection<DetectionRule> GetDetectionRules()
        {
            return _detectionRules;
        }

        
        public void UpdateDetectionRules(ObservableCollection<DetectionRule> newRules)
        {
            _detectionRules.Clear();
            foreach (var rule in newRules)
            {
                _detectionRules.Add(rule);
            }

            // Update validation
            _step2Valid = _detectionRules.Count > 0;
            UpdateNavigationButtons();
        }

        #endregion
    }

    // ✅ PERFORMANCE: Event delegates for fast step communication
    public delegate void ValidationChangedEventHandler(bool isValid);
    public delegate void DataChangedEventHandler(ApplicationInfo applicationInfo);
    public delegate Task UploadRequestedEventHandler(string installCommand, string uninstallCommand, string description, string installContext);
}