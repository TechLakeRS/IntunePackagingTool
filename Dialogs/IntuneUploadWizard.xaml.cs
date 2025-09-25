using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using IntunePackagingTool.WizardSteps;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool
{
    public partial class IntuneUploadWizard : Window, IUploadProgress
    {
        // Keep all existing properties and services
        private ApplicationInfo? _applicationInfo;
        public ApplicationInfo? ApplicationInfo
        {
            get => _applicationInfo;
            set
            {
                _applicationInfo = value;
                
            }
        }
        public string PackagePath { get; set; } = "";
        public bool IsExistingPackageMode { get; set; }
        public bool SmartModeEnabled { get; set; }

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

            
            LoadStep(0);
            this.Loaded += IntuneUploadWizard_Loaded;
        }

        private async void IntuneUploadWizard_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== Window Loaded ===");

            // Initialize based on whether it's an existing package
            await InitializeWizard();
        }

        private async Task InitializeWizard()
        {
            System.Diagnostics.Debug.WriteLine($"=== InitializeWizard ===");
            System.Diagnostics.Debug.WriteLine($"PackagePath: {PackagePath}");
            System.Diagnostics.Debug.WriteLine($"ApplicationInfo null? {ApplicationInfo == null}");

            // Check if this is an existing package
            if (!string.IsNullOrEmpty(PackagePath))
            {
                var applicationFolder = Path.Combine(PackagePath, "Application");
                var deployScript = Path.Combine(applicationFolder, "Deploy-Application.ps1");

                if (Directory.Exists(applicationFolder) && File.Exists(deployScript))
                {
                    IsExistingPackageMode = true;

                    // Make sure ApplicationInfo exists
                    if (ApplicationInfo == null)
                    {
                        ApplicationInfo = new ApplicationInfo();
                    }

                    SmartModeEnabled = await LoadFromExistingPackage();

                    if (SmartModeEnabled)
                    {
                        ShowSmartModeBanner();

                        // Update the first step with the loaded data
                        if (_stepControls[0] is AppDetailsStep appStep)
                        {
                            System.Diagnostics.Debug.WriteLine("Setting ApplicationInfo on AppDetailsStep");
                            appStep.ApplicationInfo = ApplicationInfo;
                            appStep.EnableSmartMode();
                        }
                    }
                }
            }
            else if (ApplicationInfo != null)
            {
                // For new packages
                AppSummaryText.Text = $"{ApplicationInfo.Manufacturer} {ApplicationInfo.Name} v{ApplicationInfo.Version}";

                // Update the first step
                if (_stepControls[0] is AppDetailsStep appStep)
                {
                    appStep.ApplicationInfo = ApplicationInfo;
                }

                // Add default detection rule
                _detectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = "%ProgramFiles%",
                    FileOrFolderName = $"{ApplicationInfo?.Name ?? "MyApp"}.exe",
                    CheckVersion = false
                });
            }

            UpdateStepData();
        }

        private void ShowSmartModeBanner()
        {
            // Make the smart mode banner visible
            SmartModeBanner.Visibility = Visibility.Visible;
        }

        private void UpdateUIForSmartMode()
        {
            // Update the UI to show fields are auto-detected
            if (_stepControls[0] is AppDetailsStep appStep)
            {
                appStep.EnableSmartMode();
            }
        }

        #region Step Management (High Performance)

        private void LoadStep(int stepIndex)
        {
            System.Diagnostics.Debug.WriteLine($"=== LoadStep({stepIndex}) called ===");

            // Create step if it doesn't exist
            if (_stepControls[stepIndex] == null)
            {
                System.Diagnostics.Debug.WriteLine($"Creating step {stepIndex}");
                _stepControls[stepIndex] = stepIndex switch
                {
                    0 => new AppDetailsStep(),
                    1 => new DetectionRulesStep(),
                    2 => new ReviewUploadStep(),
                    _ => throw new ArgumentException("Invalid step index")
                };

                SetupStepEvents(_stepControls[stepIndex], stepIndex);
            }

            // Set content
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
                        System.Diagnostics.Debug.WriteLine($"=== Setting up AppDetailsStep ===");
                        System.Diagnostics.Debug.WriteLine($"ApplicationInfo is null? {ApplicationInfo == null}");

                        // Set the ApplicationInfo
                        appStep.ApplicationInfo = ApplicationInfo;

                        // Set up events
                        appStep.ValidationChanged += (valid) =>
                        {
                            _step1Valid = valid;
                            UpdateNavigationButtons();
                        };

                        appStep.DataChanged += UpdateApplicationDataFromStep1;

                        System.Diagnostics.Debug.WriteLine($"AppDetailsStep setup complete");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: stepControl is not AppDetailsStep! It's {stepControl?.GetType().Name}");
                    }
                    break;
                    // ... rest of cases
            }
        }

        private void UpdateStepData()
        {
            System.Diagnostics.Debug.WriteLine($"=== UpdateStepData called ===");
            System.Diagnostics.Debug.WriteLine($"ApplicationInfo is null? {ApplicationInfo == null}");

            if (ApplicationInfo != null)
            {
                System.Diagnostics.Debug.WriteLine($"ApplicationInfo.Name: '{ApplicationInfo.Name}'");
                System.Diagnostics.Debug.WriteLine($"ApplicationInfo.Version: '{ApplicationInfo.Version}'");
                System.Diagnostics.Debug.WriteLine($"ApplicationInfo.Manufacturer: '{ApplicationInfo.Manufacturer}'");
            }

            // Share data between steps
            if (_stepControls[0] is AppDetailsStep appStep)
            {
                System.Diagnostics.Debug.WriteLine("Updating AppDetailsStep");
                appStep.ApplicationInfo = ApplicationInfo;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Step 0 is not AppDetailsStep, it's {_stepControls[0]?.GetType().Name}");
            }

            // Rest of the method...
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

        private Task ShareDataToNextStep()
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
            return Task.CompletedTask;
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
            // Don't overwrite with empty values!
            if (updatedInfo != null)
            {
                // Only update if the values are not empty
                if (!string.IsNullOrEmpty(updatedInfo.Name))
                    ApplicationInfo.Name = updatedInfo.Name;

                if (!string.IsNullOrEmpty(updatedInfo.Version))
                    ApplicationInfo.Version = updatedInfo.Version;

                if (!string.IsNullOrEmpty(updatedInfo.Manufacturer))
                    ApplicationInfo.Manufacturer = updatedInfo.Manufacturer;

                if (!string.IsNullOrEmpty(updatedInfo.InstallContext))
                    ApplicationInfo.InstallContext = updatedInfo.InstallContext;

                // Update the summary text
                AppSummaryText.Text = $"{ApplicationInfo.Manufacturer} {ApplicationInfo.Name} v{ApplicationInfo.Version}";

                // Propagate to other steps
                UpdateStepData();
            }
        }

        private async Task<bool> LoadFromExistingPackage()
        {
            try
            {
                if (string.IsNullOrEmpty(PackagePath))
                    return false;

                var scriptPath = Path.Combine(PackagePath, "Application", "Deploy-Application.ps1");
                if (!File.Exists(scriptPath))
                    return false;

                var scriptContent = await File.ReadAllTextAsync(scriptPath);

                // Make sure ApplicationInfo exists
                if (ApplicationInfo == null)
                {
                    ApplicationInfo = new ApplicationInfo();
                }

                // Extract vendor/manufacturer
                var vendorMatch = Regex.Match(scriptContent, @"\$appVendor\s*=\s*['""]([^'""]+)['""]");
                if (vendorMatch.Success)
                {
                    ApplicationInfo.Manufacturer = vendorMatch.Groups[1].Value;
                    System.Diagnostics.Debug.WriteLine($"Extracted Manufacturer: '{ApplicationInfo.Manufacturer}'");
                }

                // Extract app name
                var nameMatch = Regex.Match(scriptContent, @"\$appName\s*=\s*['""]([^'""]+)['""]");
                if (nameMatch.Success)
                {
                    ApplicationInfo.Name = nameMatch.Groups[1].Value;
                    System.Diagnostics.Debug.WriteLine($"Extracted Name: '{ApplicationInfo.Name}'");
                }

                // Extract version
                var versionMatch = Regex.Match(scriptContent, @"\$appVersion\s*=\s*['""]([^'""]+)['""]");
                if (versionMatch.Success)
                {
                    ApplicationInfo.Version = versionMatch.Groups[1].Value;
                    System.Diagnostics.Debug.WriteLine($"Extracted Version: '{ApplicationInfo.Version}'");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading from existing package: {ex.Message}");
                return false;
            }
        }

        private async Task AutoDetectDetectionRules()
        {
            _detectionRules.Clear();

            // Check for MSI files
            var applicationPath = Path.Combine(PackagePath, "Application", "Files");
            if (Directory.Exists(applicationPath))
            {
                var msiFiles = Directory.GetFiles(applicationPath, "*.msi", SearchOption.AllDirectories);
                if (msiFiles.Any())
                {
                    // Add MSI detection rule
                    _detectionRules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.MSI,
                        Path = "{PLACEHOLDER-PRODUCT-CODE}", // Will need to extract from MSI
                        FileOrFolderName = ApplicationInfo?.Version ?? "1.0.0",
                        CheckVersion = true
                    });
                }
                else
                {
                    // Add file-based detection rule
                    var exeFiles = Directory.GetFiles(applicationPath, "*.exe", SearchOption.AllDirectories);
                    if (exeFiles.Any())
                    {
                        _detectionRules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.File,
                            Path = "%ProgramFiles%\\" + (ApplicationInfo?.Name ?? "MyApp"),
                            FileOrFolderName = Path.GetFileName(exeFiles.First()),
                            CheckVersion = false
                        });
                    }
                }
            }

            // Add default if no rules detected
            if (_detectionRules.Count == 0)
            {
                _detectionRules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = "%ProgramFiles%",
                    FileOrFolderName = $"{ApplicationInfo?.Name ?? "MyApp"}.exe",
                    CheckVersion = false
                });
            }
        }

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