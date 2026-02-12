using System;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;

namespace MCPForUnity.Editor.Dependencies
{
    /// <summary>
    /// 簡單的使用者驗證，透過 Firebase Cloud Functions 檢查使用者狀態
    /// </summary>
    [InitializeOnLoad]
    public static class LicenseValidator
    {
        private const string EMAIL_KEY = "MCPForUnity.UserEmail";
        private const string BASE_URL = "https://asia-east1-unity-ai-project-edf0c.cloudfunctions.net/api";
        
        // HTTP 請求設定
        private const int REQUEST_TIMEOUT_SECONDS = 10;
        private const int REQUEST_POLL_INTERVAL_MS = 100;
        
        // 當前驗證狀態（記憶體快取，僅在編輯器運行期間有效）
        private static bool _isUserValid = false;
        private static bool _hasChecked = false;

        /// <summary>
        /// 靜態構造函數：Editor 啟動時自動驗證已儲存的 Email
        /// </summary>
        static LicenseValidator()
        {
            // 延遲執行，避免阻塞 Editor 啟動
            EditorApplication.delayCall += AutoValidateStoredEmail;
        }

        /// <summary>
        /// 自動驗證已儲存的 Email
        /// </summary>
        private static async void AutoValidateStoredEmail()
        {
            try
            {
                string email = GetStoredEmail();
                if (string.IsNullOrEmpty(email))
                {
                    // 沒有儲存的 email，不需要驗證
                    return;
                }

                McpLog.Info($"Auto-validating stored email: {email}");
                
                // 執行驗證
                var (isValid, message) = await ValidateUserAsync(email);
                
                if (isValid)
                {
                    McpLog.Info($"Auto-validation successful: {message}");
                }
                else
                {
                    McpLog.Warn($"Auto-validation failed: {message}");
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Auto-validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查使用者是否有效（簡單記憶體快取）
        /// </summary>
        public static bool IsUserValid()
        {
            return _hasChecked && _isUserValid;
        }
        
        /// <summary>
        /// 重設驗證狀態
        /// </summary>
        public static void ResetValidation()
        {
            _hasChecked = false;
            _isUserValid = false;
        }
        
        /// <summary>
        /// 取得儲存的 Email
        /// </summary>
        public static string GetStoredEmail()
        {
            return EditorPrefs.GetString(EMAIL_KEY, string.Empty);
        }
        
        /// <summary>
        /// 儲存 Email
        /// </summary>
        public static void SetStoredEmail(string email)
        {
            EditorPrefs.SetString(EMAIL_KEY, email);
        }

        /// <summary>
        /// 檢查並驗證使用者（指定 Email）
        /// 返回 (是否有效, 訊息)
        /// </summary>
        public static async Task<(bool isValid, string message)> ValidateUserAsync(string email)
        {
            try
            {
                var result = await CheckUserAsync(email);
                
                if (result.exists)
                {
                    if (result.isBlacklisted)
                    {
                        _hasChecked = true;
                        _isUserValid = false;
                        EditorPrefs.DeleteKey(EMAIL_KEY);
                        return (false, "此帳號已被停用");
                    }
                    else
                    {
                        _hasChecked = true;
                        _isUserValid = true;
                        return (true, "驗證成功");
                    }
                }
                else
                {
                    _hasChecked = true;
                    _isUserValid = false;
                    EditorPrefs.DeleteKey(EMAIL_KEY);
                    return (false, "使用者未註冊");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"User validation failed: {ex.Message}");
                _hasChecked = true;
                _isUserValid = false;
                EditorPrefs.DeleteKey(EMAIL_KEY);
                return (false, $"驗證失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 發送 POST 請求到 Firebase API（共用方法）
        /// </summary>
        private static async Task<string> SendPostRequestAsync(string endpoint, EmailPayload payload)
        {
            string url = $"{BASE_URL}/{endpoint}";
            string json = JsonConvert.SerializeObject(payload);
            
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = REQUEST_TIMEOUT_SECONDS;
                
                // 發送請求
                var operation = request.SendWebRequest();
                
                // 等待完成
                while (!operation.isDone)
                {
                    await Task.Delay(REQUEST_POLL_INTERVAL_MS);
                }

#if UNITY_2020_1_OR_NEWER
                if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    throw new Exception($"API request failed: {request.error}");
                }

                return request.downloadHandler.text;
            }
        }

        /// <summary>
        /// 檢查使用者是否已註冊（呼叫 Firebase Cloud Functions）
        /// </summary>
        private static async Task<CheckUserResponse> CheckUserAsync(string email)
        {
            string responseText = await SendPostRequestAsync("checkUser", new EmailPayload { email = email });
            return JsonConvert.DeserializeObject<CheckUserResponse>(responseText);
        }

        /// <summary>
        /// 記錄使用者使用紀錄
        /// </summary>
        public static async Task<bool> RecordUsageAsync(string email)
        {
            try
            {
                if (string.IsNullOrEmpty(email))
                {
                    McpLog.Warn("Cannot record usage: email is empty");
                    return false;
                }

                McpLog.Info($"Recording usage for {email}...");
                
                await SendPostRequestAsync("recordUsage", new EmailPayload { email = email });

                McpLog.Info($"Usage recorded for {email}");
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Record usage API request failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 記錄使用紀錄（Fire-and-forget，不等待結果）
        /// </summary>
        public static void RecordUsage(string email)
        {
            // 非同步執行，不阻塞主線程
            _ = RecordUsageAsync(email);
        }

        // JSON 資料結構
        [Serializable]
        private class EmailPayload
        {
            public string email;
        }

        [Serializable]
        private class CheckUserResponse
        {
            public bool exists;
            public bool isBlacklisted;
            
            // 支援兩種命名格式
            [JsonProperty("is_blacklisted")]
            private bool _isBlacklistedSnake
            {
                set => isBlacklisted = value;
            }
        }
    }
}
