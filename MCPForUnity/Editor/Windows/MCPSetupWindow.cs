using System;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Setup window for checking and guiding dependency installation
    /// </summary>
    public class MCPSetupWindow : EditorWindow
    {
        // UI Elements
        private VisualElement pythonIndicator;
        private Label pythonVersion;
        private Label pythonDetails;
        private VisualElement uvIndicator;
        private Label uvVersion;
        private Label uvDetails;
        private Label statusMessage;
        private VisualElement installationSection;
        private Label installationInstructions;
        private Button openPythonLinkButton;
        private Button openUvLinkButton;
        private Button refreshButton;
        private Button doneButton;
        
        // Email verification UI Elements
        private TextField emailField;
        private Button validateEmailButton;
        private Label emailStatusLabel;
        private VisualElement emailStatusIndicator;

        private DependencyCheckResult _dependencyResult;
        private bool _isCheckingEmail;

        public static void ShowWindow(DependencyCheckResult dependencyResult = null)
        {
            var window = GetWindow<MCPSetupWindow>("MCP Setup");
            window.minSize = new Vector2(480, 520);  // 增加高度以容納 Email 驗證區塊
            // window.maxSize = new Vector2(600, 700);
            window._dependencyResult = dependencyResult ?? DependencyManager.CheckAllDependencies();
            window.Show();
        }

        public void CreateGUI()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{basePath}/Editor/Windows/MCPSetupWindow.uxml"
            );

            if (visualTree == null)
            {
                McpLog.Error($"Failed to load UXML at: {basePath}/Editor/Windows/MCPSetupWindow.uxml");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Cache UI elements
            pythonIndicator = rootVisualElement.Q<VisualElement>("python-indicator");
            pythonVersion = rootVisualElement.Q<Label>("python-version");
            pythonDetails = rootVisualElement.Q<Label>("python-details");
            uvIndicator = rootVisualElement.Q<VisualElement>("uv-indicator");
            uvVersion = rootVisualElement.Q<Label>("uv-version");
            uvDetails = rootVisualElement.Q<Label>("uv-details");
            statusMessage = rootVisualElement.Q<Label>("status-message");
            installationSection = rootVisualElement.Q<VisualElement>("installation-section");
            installationInstructions = rootVisualElement.Q<Label>("installation-instructions");
            openPythonLinkButton = rootVisualElement.Q<Button>("open-python-link-button");
            openUvLinkButton = rootVisualElement.Q<Button>("open-uv-link-button");
            refreshButton = rootVisualElement.Q<Button>("refresh-button");
            doneButton = rootVisualElement.Q<Button>("done-button");

            // Cache email verification UI elements
            emailField = rootVisualElement.Q<TextField>("email-field");
            validateEmailButton = rootVisualElement.Q<Button>("validate-email-button");
            emailStatusLabel = rootVisualElement.Q<Label>("email-status-label");
            emailStatusIndicator = rootVisualElement.Q<VisualElement>("email-status-indicator");

            // Register callbacks
            refreshButton.clicked += OnRefreshClicked;
            doneButton.clicked += OnDoneClicked;
            openPythonLinkButton.clicked += OnOpenPythonInstallClicked;
            openUvLinkButton.clicked += OnOpenUvInstallClicked;

            // Register email verification callbacks
            if (validateEmailButton != null)
            {
                validateEmailButton.clicked += OnValidateEmailClicked;
            }

            // Initialize email field with stored value
            if (emailField != null)
            {
                emailField.value = LicenseValidator.GetStoredEmail();
            }

            // Initial update
            UpdateUI();
            UpdateEmailStatus();
        }

        private void OnEnable()
        {
            if (_dependencyResult == null)
            {
                _dependencyResult = DependencyManager.CheckAllDependencies();
            }
        }

        private void OnRefreshClicked()
        {
            _dependencyResult = DependencyManager.CheckAllDependencies();
            UpdateUI();
        }

        private void OnDoneClicked()
        {
            Setup.SetupWindowService.MarkSetupCompleted();
            Close();
        }

        private void OnOpenPythonInstallClicked()
        {
            var (pythonUrl, _) = DependencyManager.GetInstallationUrls();
            Application.OpenURL(pythonUrl);
        }

        private void OnOpenUvInstallClicked()
        {
            var (_, uvUrl) = DependencyManager.GetInstallationUrls();
            Application.OpenURL(uvUrl);
        }

        private void UpdateUI()
        {
            if (_dependencyResult == null)
                return;

            // Update Python status
            var pythonDep = _dependencyResult.Dependencies.Find(d => d.Name == "Python");
            if (pythonDep != null)
            {
                UpdateDependencyStatus(pythonIndicator, pythonVersion, pythonDetails, pythonDep);
            }

            // Update uv status
            var uvDep = _dependencyResult.Dependencies.Find(d => d.Name == "uv Package Manager");
            if (uvDep != null)
            {
                UpdateDependencyStatus(uvIndicator, uvVersion, uvDetails, uvDep);
            }

            // Update overall status
            if (_dependencyResult.IsSystemReady)
            {
                statusMessage.text = "✓ All requirements met! MCP for Unity is ready to use.";
                statusMessage.style.color = new StyleColor(Color.green);
                installationSection.style.display = DisplayStyle.None;
            }
            else
            {
                statusMessage.text = "⚠ Missing dependencies. MCP for Unity requires all dependencies to function.";
                statusMessage.style.color = new StyleColor(new Color(1f, 0.6f, 0f)); // Orange
                installationSection.style.display = DisplayStyle.Flex;
                installationInstructions.text = DependencyManager.GetInstallationRecommendations();
            }
        }

        private void UpdateDependencyStatus(VisualElement indicator, Label versionLabel, Label detailsLabel, DependencyStatus dep)
        {
            if (dep.IsAvailable)
            {
                indicator.RemoveFromClassList("invalid");
                indicator.AddToClassList("valid");
                versionLabel.text = $"v{dep.Version}";
                detailsLabel.text = dep.Details ?? "Available";
                detailsLabel.style.color = new StyleColor(Color.gray);
            }
            else
            {
                indicator.RemoveFromClassList("valid");
                indicator.AddToClassList("invalid");
                versionLabel.text = "Not Found";
                detailsLabel.text = dep.ErrorMessage ?? "Not available";
                detailsLabel.style.color = new StyleColor(Color.red);
            }
        }

        private async void OnValidateEmailClicked()
        {
            if (_isCheckingEmail) return;
            
            string email = emailField.value?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                EditorUtility.DisplayDialog("錯誤", "請輸入 Email 地址", "確定");
                return;
            }
            
            if (!IsValidEmail(email))
            {
                EditorUtility.DisplayDialog("錯誤", "請輸入有效的 Email 地址", "確定");
                return;
            }
            
            _isCheckingEmail = true;
            validateEmailButton.SetEnabled(false);
            emailStatusLabel.text = "檢查中...";
            emailStatusIndicator.RemoveFromClassList("valid");
            emailStatusIndicator.RemoveFromClassList("invalid");
            
            try
            {
                // 先重設驗證狀態（disable ToggleMCPWindow）
                LicenseValidator.ResetValidation();
                
                // 驗證使用者
                var (isValid, message) = await LicenseValidator.ValidateUserAsync(email);
                
                if (isValid)
                {
                    // 儲存 Email
                    LicenseValidator.SetStoredEmail(email);
                    emailStatusLabel.text = message;
                    emailStatusIndicator.AddToClassList("valid");
                    emailStatusIndicator.RemoveFromClassList("invalid");
                }
                else
                {
                    emailStatusLabel.text = message;
                    emailStatusIndicator.RemoveFromClassList("valid");
                    emailStatusIndicator.AddToClassList("invalid");
                }
            }
            catch (Exception ex)
            {
                emailStatusLabel.text = $"錯誤: {ex.Message}";
                emailStatusIndicator.RemoveFromClassList("valid");
                emailStatusIndicator.AddToClassList("invalid");
                McpLog.Error($"Email validation error: {ex.Message}");
            }
            finally
            {
                _isCheckingEmail = false;
                validateEmailButton.SetEnabled(true);
            }
        }
        
        private void UpdateEmailStatus()
        {
            if (emailStatusLabel == null || emailStatusIndicator == null)
            {
                return;
            }

            bool isValid = LicenseValidator.IsUserValid();
            
            if (isValid)
            {
                emailStatusLabel.text = "已驗證 ✓";
                emailStatusIndicator.AddToClassList("valid");
                emailStatusIndicator.RemoveFromClassList("invalid");
            }
            else
            {
                emailStatusLabel.text = "未驗證";
                emailStatusIndicator.RemoveFromClassList("valid");
                emailStatusIndicator.AddToClassList("invalid");
            }
        }
        
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
