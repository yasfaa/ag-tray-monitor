using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.IO.Path;

namespace AGQuotaTracker
{
    public class QuotaModel
    {
        public double Pct { get; set; }
        public double Remaining { get; set; }
        public string ResetStr { get; set; }
        public string ResetTime { get; set; }

        public QuotaModel()
        {
            Pct = 100.0;
            Remaining = 1.0;
            ResetStr = "Full";
            ResetTime = null;
        }
    }

    public class UsageData
    {
        public bool IsConnected { get; set; }
        public string SourceMode { get; set; } // "Local" or "Cloud"
        public string ActiveAccount { get; set; } // "local" or email
        public List<string> AvailableAccounts { get; set; }
        public string BaseUrl { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }
        public string Plan { get; set; }
        public QuotaModel Gemini5h { get; set; }
        public QuotaModel GeminiWk { get; set; }
        public QuotaModel Claude5h { get; set; }
        public QuotaModel ClaudeWk { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Error { get; set; }

        public UsageData()
        {
            SourceMode = "Local";
            ActiveAccount = "local";
            AvailableAccounts = new List<string>();
            UserName = "User";
            UserEmail = "Local Antigravity";
            Plan = "Pro";
            Gemini5h = new QuotaModel();
            GeminiWk = new QuotaModel();
            Claude5h = new QuotaModel();
            ClaudeWk = new QuotaModel();
            LastUpdated = DateTime.Now;
        }
    }

    public class StoredTokens
    {
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public long expiresAt { get; set; }
        public string email { get; set; }
        public string projectId { get; set; }
    }

    public class AccountMetadata
    {
        public string email { get; set; }
        public string addedAt { get; set; }
        public string lastUsed { get; set; }
    }

    public class AccountStorage
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long DateTimeToUnixMs(DateTime dt)
        {
            return (long)(dt.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        public static DateTime UnixMsToDateTime(long ms)
        {
            return UnixEpoch.AddMilliseconds(ms);
        }

        public static string GetConfigDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "antigravity-usage");
        }

        public static string GetAccountsDir()
        {
            return Path.Combine(GetConfigDir(), "accounts");
        }

        public static List<string> GetAccountEmails()
        {
            var list = new List<string>();
            string accountsDir = GetAccountsDir();
            if (Directory.Exists(accountsDir))
            {
                string[] dirs = Directory.GetDirectories(accountsDir);
                foreach (string d in dirs)
                {
                    string tokenFile = Path.Combine(d, "tokens.json");
                    if (File.Exists(tokenFile))
                    {
                        list.Add(Path.GetFileName(d));
                    }
                }
            }
            return list;
        }

        public static StoredTokens LoadTokens(string email)
        {
            string path = Path.Combine(GetAccountsDir(), email, "tokens.json");
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var js = new JavaScriptSerializer();
                return js.Deserialize<StoredTokens>(json);
            }
            catch { return null; }
        }

        public static void SaveTokens(string email, StoredTokens tokens)
        {
            string dir = Path.Combine(GetAccountsDir(), email);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "tokens.json");
            var js = new JavaScriptSerializer();
            string json = js.Serialize(tokens);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static void SaveMetadata(string email, AccountMetadata meta)
        {
            string dir = Path.Combine(GetAccountsDir(), email);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "metadata.json");
            var js = new JavaScriptSerializer();
            string json = js.Serialize(meta);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static string GetActiveAccount()
        {
            string configPath = Path.Combine(GetConfigDir(), "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath, Encoding.UTF8);
                    var js = new JavaScriptSerializer();
                    var dict = js.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null && dict.ContainsKey("activeAccount") && dict["activeAccount"] != null)
                    {
                        return dict["activeAccount"].ToString();
                    }
                }
                catch { }
            }
            return null;
        }

        public static void SetActiveAccount(string account)
        {
            string dir = GetConfigDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string configPath = Path.Combine(dir, "config.json");
            var dict = new Dictionary<string, object>();
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath, Encoding.UTF8);
                    var js = new JavaScriptSerializer();
                    dict = js.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                catch { }
            }
            dict["version"] = "2.0";
            dict["activeAccount"] = account;
            var jsOut = new JavaScriptSerializer();
            File.WriteAllText(configPath, jsOut.Serialize(dict), Encoding.UTF8);
        }

        public static bool RemoveAccount(string email)
        {
            try
            {
                string dir = Path.Combine(GetAccountsDir(), email);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    if (string.Equals(GetActiveAccount(), email, StringComparison.OrdinalIgnoreCase))
                    {
                        var remaining = GetAccountEmails();
                        SetActiveAccount(remaining.Count > 0 ? remaining[0] : "local");
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    public class CloudCodeClient
    {
        public const string OAuthClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
        public const string OAuthClientSecret = "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";
        public const string CloudCodeBaseUrl = "https://cloudcode-pa.googleapis.com";

        public static StoredTokens EnsureValidToken(string email)
        {
            var tokens = AccountStorage.LoadTokens(email);
            if (tokens == null) throw new Exception("Account tokens not found for: " + email);

            if (string.IsNullOrEmpty(tokens.refreshToken))
            {
                if (DateTime.UtcNow >= AccountStorage.UnixMsToDateTime(tokens.expiresAt))
                {
                    throw new Exception("Access token expired and no refresh token available. Please login again.");
                }
                return tokens;
            }

            // Refresh if expired or expiring within 5 minutes
            if (DateTime.UtcNow >= AccountStorage.UnixMsToDateTime(tokens.expiresAt).AddMinutes(-5))
            {
                var req = (HttpWebRequest)WebRequest.Create("https://oauth2.googleapis.com/token");
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.Timeout = 10000;
                string postData = string.Format("refresh_token={0}&client_id={1}&client_secret={2}&grant_type=refresh_token",
                    Uri.EscapeDataString(tokens.refreshToken),
                    Uri.EscapeDataString(OAuthClientId),
                    Uri.EscapeDataString(OAuthClientSecret));
                byte[] bytes = Encoding.UTF8.GetBytes(postData);
                using (var stream = req.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    var js = new JavaScriptSerializer();
                    var dict = js.Deserialize<Dictionary<string, object>>(json);
                    if (dict.ContainsKey("access_token"))
                    {
                        tokens.accessToken = dict["access_token"].ToString();
                        int expiresIn = dict.ContainsKey("expires_in") ? Convert.ToInt32(dict["expires_in"]) : 3600;
                        tokens.expiresAt = AccountStorage.DateTimeToUnixMs(DateTime.UtcNow.AddSeconds(expiresIn));
                        AccountStorage.SaveTokens(email, tokens);
                    }
                }
            }

            return tokens;
        }

        public static string ResolveProjectId(string accessToken)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(CloudCodeBaseUrl + "/v1internal:loadCodeAssist");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "antigravity";
                req.Headers.Add("Authorization", "Bearer " + accessToken);
                req.Timeout = 8000;
                string metaJson = "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(metaJson);
                using (var st = req.GetRequestStream()) { st.Write(bytes, 0, bytes.Length); }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    if (dict != null && dict.ContainsKey("cloudaicompanionProject"))
                    {
                        var projObj = dict["cloudaicompanionProject"];
                        if (projObj is string) return projObj.ToString();
                        if (projObj is Dictionary<string, object>)
                        {
                            var pDict = (Dictionary<string, object>)projObj;
                            if (pDict.ContainsKey("id") && pDict["id"] != null) return pDict["id"].ToString();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static UsageData FetchUsage(string email)
        {
            var tokens = EnsureValidToken(email);

            string planType = "Pro";
            string projectId = tokens.projectId;

            // 1. loadCodeAssist
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(CloudCodeBaseUrl + "/v1internal:loadCodeAssist");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "antigravity";
                req.Headers.Add("Authorization", "Bearer " + tokens.accessToken);
                req.Timeout = 8000;
                string metaJson = "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(metaJson);
                using (var st = req.GetRequestStream()) { st.Write(bytes, 0, bytes.Length); }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    if (dict != null)
                    {
                        if (dict.ContainsKey("planInfo") && dict["planInfo"] is Dictionary<string, object>)
                        {
                            var pi = (Dictionary<string, object>)dict["planInfo"];
                            if (pi.ContainsKey("planType") && pi["planType"] != null && !string.IsNullOrEmpty(pi["planType"].ToString()))
                            {
                                planType = pi["planType"].ToString();
                            }
                        }
                        if (dict.ContainsKey("cloudaicompanionProject"))
                        {
                            var projObj = dict["cloudaicompanionProject"];
                            string foundId = null;
                            if (projObj is string) foundId = projObj.ToString();
                            else if (projObj is Dictionary<string, object>)
                            {
                                var pDict = (Dictionary<string, object>)projObj;
                                if (pDict.ContainsKey("id") && pDict["id"] != null) foundId = pDict["id"].ToString();
                            }
                            if (!string.IsNullOrEmpty(foundId))
                            {
                                projectId = foundId;
                                if (tokens.projectId != foundId)
                                {
                                    tokens.projectId = foundId;
                                    AccountStorage.SaveTokens(email, tokens);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. fetchAvailableModels
            var modelReq = (HttpWebRequest)WebRequest.Create(CloudCodeBaseUrl + "/v1internal:fetchAvailableModels");
            modelReq.Method = "POST";
            modelReq.ContentType = "application/json";
            modelReq.UserAgent = "antigravity";
            modelReq.Headers.Add("Authorization", "Bearer " + tokens.accessToken);
            modelReq.Timeout = 8000;
            string mBody = !string.IsNullOrEmpty(projectId)
                ? string.Format("{{\"project\":\"{0}\"}}", projectId)
                : "{}";
            byte[] mBytes = Encoding.UTF8.GetBytes(mBody);
            using (var st = modelReq.GetRequestStream()) { st.Write(mBytes, 0, mBytes.Length); }

            Dictionary<string, object> modelsDict = null;
            using (var resp = (HttpWebResponse)modelReq.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                if (dict != null && dict.ContainsKey("models") && dict["models"] is Dictionary<string, object>)
                {
                    modelsDict = (Dictionary<string, object>)dict["models"];
                }
            }

            QuotaModel gemini5h = null;
            QuotaModel claude5h = null;

            if (modelsDict != null)
            {
                foreach (var kvp in modelsDict)
                {
                    string mId = kvp.Key.ToLower();
                    var mInfo = kvp.Value as Dictionary<string, object>;
                    if (mInfo == null || !mInfo.ContainsKey("quotaInfo")) continue;
                    var qInfo = mInfo["quotaInfo"] as Dictionary<string, object>;
                    if (qInfo == null) continue;

                    double rem = 1.0;
                    if (qInfo.ContainsKey("remainingFraction") && qInfo["remainingFraction"] != null)
                    {
                        rem = Convert.ToDouble(qInfo["remainingFraction"]);
                    }
                    string resetTime = qInfo.ContainsKey("resetTime") && qInfo["resetTime"] != null ? qInfo["resetTime"].ToString() : null;
                    string resetStr = ApiClient.FormatRemainingTime(resetTime);

                    var qm = new QuotaModel();
                    qm.Remaining = rem;
                    qm.Pct = Math.Round(rem * 100.0, 1);
                    qm.ResetTime = resetTime;
                    qm.ResetStr = resetStr;

                    bool isGemini = mId.Contains("gemini");
                    bool isClaude = mId.Contains("claude") || mId.Contains("sonnet") || mId.Contains("opus") || mId.Contains("gpt");

                    if (isGemini)
                    {
                        if (gemini5h == null || mId == "gemini-3-flash" || mId == "gemini-3.1-pro-high")
                        {
                            gemini5h = qm;
                        }
                    }
                    else if (isClaude)
                    {
                        if (claude5h == null || mId == "claude-sonnet-4-6" || mId.Contains("claude-opus"))
                        {
                            claude5h = qm;
                        }
                    }
                }
            }

            var result = new UsageData();
            result.IsConnected = true;
            result.SourceMode = "Cloud";
            result.ActiveAccount = email;
            result.UserEmail = email;
            result.UserName = email.Contains("@") ? email.Split('@')[0] : email;
            result.Plan = planType;
            result.BaseUrl = "https://antigravity.google";
            result.LastUpdated = DateTime.Now;

            result.Gemini5h = gemini5h ?? new QuotaModel();
            result.GeminiWk = new QuotaModel { Pct = result.Gemini5h.Pct, Remaining = result.Gemini5h.Remaining, ResetStr = "Cloud Pool (5h rolling)" };

            result.Claude5h = claude5h ?? new QuotaModel();
            result.ClaudeWk = new QuotaModel { Pct = result.Claude5h.Pct, Remaining = result.Claude5h.Remaining, ResetStr = "Cloud Pool (5h rolling)" };

            result.AvailableAccounts = AccountStorage.GetAccountEmails();
            return result;
        }
    }

    public class GoogleOAuth
    {
        public static void StartOAuthLogin(Action<bool, string> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpListener listener = null;
                try
                {
                    int port = 0;
                    var tcp = new TcpListener(IPAddress.Loopback, 0);
                    tcp.Start();
                    port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                    tcp.Stop();

                    string redirectUri = string.Format("http://127.0.0.1:{0}/callback/", port);
                    string state = Guid.NewGuid().ToString("N");

                    listener = new HttpListener();
                    listener.Prefixes.Add(redirectUri);
                    listener.Start();

                    string authUrl = string.Format(
                        "https://accounts.google.com/o/oauth2/v2/auth?client_id={0}&redirect_uri={1}&response_type=code&scope={2}&access_type=offline&prompt=consent&state={3}",
                        Uri.EscapeDataString(CloudCodeClient.OAuthClientId),
                        Uri.EscapeDataString(redirectUri),
                        Uri.EscapeDataString("https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/userinfo.email"),
                        Uri.EscapeDataString(state)
                    );

                    Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                    var asyncResult = listener.BeginGetContext(null, null);
                    bool signaled = asyncResult.AsyncWaitHandle.WaitOne(120000);
                    if (!signaled)
                    {
                        try { listener.Stop(); } catch { }
                        if (onComplete != null) onComplete(false, "Login timed out.");
                        return;
                    }

                    var context = listener.EndGetContext(asyncResult);
                    var req = context.Request;
                    var res = context.Response;

                    string code = req.QueryString["code"];
                    string returnedState = req.QueryString["state"];
                    string error = req.QueryString["error"];

                    string html = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Antigravity Quota Monitor</title>"
                        + "<style>body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#0F172A;color:#F8FAFC;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;}"
                        + ".card{background:#1E293B;padding:36px;border-radius:12px;border:1px solid #334155;text-align:center;box-shadow:0 10px 25px rgba(0,0,0,0.5);max-width:400px;}"
                        + "h1{color:#10B981;font-size:22px;margin-bottom:8px;}"
                        + "p{color:#94A3B8;font-size:14px;line-height:1.5;}</style></head>"
                        + "<body><div class='card'><h1>&#10004; Account Connected!</h1><p>You can close this tab and return to <strong>Antigravity Quota Monitor</strong>.</p></div></body></html>";
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                    res.ContentType = "text/html; charset=utf-8";
                    res.ContentLength64 = htmlBytes.Length;
                    res.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                    res.OutputStream.Close();
                    try { listener.Stop(); } catch { }

                    if (!string.IsNullOrEmpty(error))
                    {
                        if (onComplete != null) onComplete(false, "Google Auth error: " + error);
                        return;
                    }

                    if (string.IsNullOrEmpty(code) || returnedState != state)
                    {
                        if (onComplete != null) onComplete(false, "Invalid authorization code or state mismatch.");
                        return;
                    }

                    // Exchange code for tokens
                    var tokenReq = (HttpWebRequest)WebRequest.Create("https://oauth2.googleapis.com/token");
                    tokenReq.Method = "POST";
                    tokenReq.ContentType = "application/x-www-form-urlencoded";
                    string postData = string.Format("code={0}&client_id={1}&client_secret={2}&redirect_uri={3}&grant_type=authorization_code",
                        Uri.EscapeDataString(code),
                        Uri.EscapeDataString(CloudCodeClient.OAuthClientId),
                        Uri.EscapeDataString(CloudCodeClient.OAuthClientSecret),
                        Uri.EscapeDataString(redirectUri));
                    byte[] postBytes = Encoding.UTF8.GetBytes(postData);
                    using (var st = tokenReq.GetRequestStream()) { st.Write(postBytes, 0, postBytes.Length); }

                    string tokenJson;
                    using (var tokenResp = (HttpWebResponse)tokenReq.GetResponse())
                    using (var reader = new StreamReader(tokenResp.GetResponseStream(), Encoding.UTF8))
                    {
                        tokenJson = reader.ReadToEnd();
                    }

                    var js = new JavaScriptSerializer();
                    var tokenDict = js.Deserialize<Dictionary<string, object>>(tokenJson);
                    string accessToken = tokenDict.ContainsKey("access_token") ? tokenDict["access_token"].ToString() : "";
                    string refreshToken = tokenDict.ContainsKey("refresh_token") ? tokenDict["refresh_token"].ToString() : "";
                    int expiresIn = tokenDict.ContainsKey("expires_in") ? Convert.ToInt32(tokenDict["expires_in"]) : 3600;

                    if (string.IsNullOrEmpty(accessToken))
                    {
                        if (onComplete != null) onComplete(false, "Failed to obtain access token.");
                        return;
                    }

                    string userEmail = null;
                    try
                    {
                        var userReq = (HttpWebRequest)WebRequest.Create("https://www.googleapis.com/oauth2/v2/userinfo");
                        userReq.Headers.Add("Authorization", "Bearer " + accessToken);
                        using (var uResp = userReq.GetResponse())
                        using (var reader = new StreamReader(uResp.GetResponseStream(), Encoding.UTF8))
                        {
                            var uDict = js.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                            if (uDict.ContainsKey("email") && uDict["email"] != null) userEmail = uDict["email"].ToString();
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(userEmail))
                    {
                        if (onComplete != null) onComplete(false, "Failed to retrieve Google user email.");
                        return;
                    }

                    string projectId = CloudCodeClient.ResolveProjectId(accessToken);

                    var stored = new StoredTokens();
                    stored.accessToken = accessToken;
                    stored.refreshToken = refreshToken;
                    stored.expiresAt = AccountStorage.DateTimeToUnixMs(DateTime.UtcNow.AddSeconds(expiresIn));
                    stored.email = userEmail;
                    stored.projectId = projectId;

                    AccountStorage.SaveTokens(userEmail, stored);
                    AccountStorage.SaveMetadata(userEmail, new AccountMetadata { email = userEmail, addedAt = DateTime.UtcNow.ToString("o"), lastUsed = DateTime.UtcNow.ToString("o") });
                    AccountStorage.SetActiveAccount(userEmail);

                    if (onComplete != null) onComplete(true, userEmail);
                }
                catch (Exception ex)
                {
                    if (listener != null) { try { listener.Stop(); } catch { } }
                    if (onComplete != null) onComplete(false, ex.Message);
                }
            });
        }
    }

    public class ApiClient
    {
        private static string _cachedEndpoint;
        private static string _cachedCsrf;
        private static Process _spawnedDaemon;

        public static string CachedEndpoint
        {
            get { return _cachedEndpoint; }
        }

        static ApiClient()
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            // TLS 1.2 = 3072, TLS 1.1 = 768, TLS 1.0 = 192
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
        }

        public static string FormatRemainingTime(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "Full";
            try
            {
                DateTime target = DateTime.Parse(isoDate).ToUniversalTime();
                DateTime now = DateTime.UtcNow;
                TimeSpan diff = target - now;
                if (diff.TotalSeconds <= 0) return "Ready to reset";
                if (diff.TotalDays >= 1) return string.Format("{0}d {1}h", (int)diff.TotalDays, diff.Hours);
                if (diff.TotalHours >= 1) return string.Format("{0}h {1}m", diff.Hours, diff.Minutes);
                return string.Format("{0}m", Math.Max(1, diff.Minutes));
            }
            catch
            {
                return isoDate;
            }
        }

        public static Dictionary<string, object> InvokeRpc(string baseUrl, string path, string csrf, object bodyObj)
        {
            var js = new JavaScriptSerializer();
            string jsonBody = bodyObj != null ? js.Serialize(bodyObj) : "{}";
            byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);

            var req = (HttpWebRequest)WebRequest.Create(baseUrl + path);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 3000;
            req.ReadWriteTimeout = 3000;
            req.Headers.Add("Connect-Protocol-Version", "1");
            if (!string.IsNullOrEmpty(csrf))
            {
                req.Headers.Add("X-Codeium-Csrf-Token", csrf);
            }

            using (var stream = req.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string resStr = reader.ReadToEnd();
                return js.Deserialize<Dictionary<string, object>>(resStr);
            }
        }

        private static string FindLanguageServerBinary()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] candidates = new string[]
            {
                Path.Combine(localAppData, @"Programs\antigravity\resources\bin\language_server.exe"),
                Path.Combine(progFiles, @"Antigravity\resources\bin\language_server.exe"),
                Path.Combine(progFilesX86, @"Antigravity\resources\bin\language_server.exe"),
                Path.Combine(localAppData, @"Programs\antigravity-ide\resources\bin\language_server.exe"),
                Path.Combine(userProfile, @".gemini\antigravity\resources\bin\language_server.exe")
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint remoteAddr;
            public byte remotePort1;
            public byte remotePort2;
            public byte remotePort3;
            public byte remotePort4;
            public int owningPid;

            public int LocalPort
            {
                get { return (localPort1 << 8) + localPort2; }
            }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

        private static List<int> GetListeningPortsForPid(int pid)
        {
            var ports = new List<int>();
            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, 2, 5, 0);
            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, 2, 5, 0) == 0)
                {
                    int rowCount = Marshal.ReadInt32(tcpTablePtr);
                    IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + 4);
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));
                        if (row.owningPid == pid && row.state == 2)
                        {
                            ports.Add(row.LocalPort);
                        }
                        rowPtr = (IntPtr)((long)rowPtr + rowSize);
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }
            return ports.Distinct().ToList();
        }

        public static void StopHeadlessDaemon()
        {
            if (_spawnedDaemon != null && !_spawnedDaemon.HasExited)
            {
                try { _spawnedDaemon.Kill(); } catch { }
                _spawnedDaemon = null;
            }
        }

        private static bool TestEndpoint(string baseUrl, string csrf, out Dictionary<string, object> quota)
        {
            quota = null;
            try
            {
                var test = InvokeRpc(baseUrl, "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary", csrf, null);
                if (test != null && test.ContainsKey("response"))
                {
                    var resp = test["response"] as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("groups"))
                    {
                        quota = test;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryStartHeadlessDaemon(out string baseUrl, out string csrf, out Dictionary<string, object> quota)
        {
            baseUrl = null;
            csrf = null;
            quota = null;

            if (_spawnedDaemon != null && !_spawnedDaemon.HasExited && !string.IsNullOrEmpty(_cachedEndpoint) && !string.IsNullOrEmpty(_cachedCsrf))
            {
                if (TestEndpoint(_cachedEndpoint, _cachedCsrf, out quota))
                {
                    baseUrl = _cachedEndpoint;
                    csrf = _cachedCsrf;
                    return true;
                }
            }

            string bin = FindLanguageServerBinary();
            if (string.IsNullOrEmpty(bin)) return false;

            string newCsrf = Guid.NewGuid().ToString();
            string args = string.Format(
                "--standalone --override_ide_name antigravity --subclient_type hub --override_ide_version 2.17.0 " +
                "--override_user_agent_name antigravity --https_server_port 0 --csrf_token {0} " +
                "--app_data_dir antigravity --api_server_url https://generativelanguage.googleapis.com " +
                "--cloud_code_endpoint https://daily-cloudcode-pa.googleapis.com", newCsrf);

            try
            {
                var psi = new ProcessStartInfo(bin, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                _spawnedDaemon = Process.Start(psi);
                _cachedCsrf = newCsrf;
                Thread.Sleep(1500);

                var ports = GetListeningPortsForPid(_spawnedDaemon.Id);
                foreach (int port in ports)
                {
                    foreach (string proto in new string[] { "https", "http" })
                    {
                        string candidateUrl = string.Format("{0}://127.0.0.1:{1}", proto, port);
                        if (TestEndpoint(candidateUrl, newCsrf, out quota))
                        {
                            baseUrl = candidateUrl;
                            csrf = newCsrf;
                            _cachedEndpoint = candidateUrl;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private class ProcCandidate
        {
            public int Pid { get; set; }
            public string CommandLine { get; set; }
            public int Score { get; set; }
        }

        public static bool FindConnection(out string baseUrl, out string csrf, out Dictionary<string, object> initialQuota)
        {
            baseUrl = null;
            csrf = null;
            initialQuota = null;

            // 1. Cached check
            if (!string.IsNullOrEmpty(_cachedEndpoint) && !string.IsNullOrEmpty(_cachedCsrf))
            {
                if (TestEndpoint(_cachedEndpoint, _cachedCsrf, out initialQuota))
                {
                    baseUrl = _cachedEndpoint;
                    csrf = _cachedCsrf;
                    return true;
                }
                _cachedEndpoint = null;
                _cachedCsrf = null;
            }

            // 2. Query Running Processes
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE CommandLine LIKE '%language_server%' OR CommandLine LIKE '%--hub-port%'"))
                using (var results = searcher.Get())
                {
                    var procs = new List<ProcCandidate>();
                    foreach (ManagementObject mo in results)
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        string cmd = mo["CommandLine"] != null ? mo["CommandLine"].ToString() : "";
                        if (string.IsNullOrEmpty(cmd)) continue;

                        int score = 0;
                        string cmdLower = cmd.ToLower();
                        if (cmdLower.Contains("--standalone")) score += 10;
                        if (cmdLower.Contains(@"antigravity\resources\bin\language_server.exe")) score += 8;
                        if (cmdLower.Contains("--csrf_token")) score += 5;
                        if (cmdLower.Contains("--hub-port")) score += 4;

                        var cand = new ProcCandidate();
                        cand.Pid = pid;
                        cand.CommandLine = cmd;
                        cand.Score = score;
                        procs.Add(cand);
                    }

                    var sortedProcs = procs.OrderByDescending(delegate(ProcCandidate p) { return p.Score; }).ToList();

                    foreach (var p in sortedProcs)
                    {
                        int pid = p.Pid;
                        string cmd = p.CommandLine;

                        string candCsrf = null;
                        var mCsrf = Regex.Match(cmd, @"--csrf_token(?:=|\s+)(?:""([^""]+)""|'([^']+)'|([^\s""']+))");
                        if (mCsrf.Success)
                        {
                            candCsrf = (mCsrf.Groups[1].Value + mCsrf.Groups[2].Value + mCsrf.Groups[3].Value).Trim();
                        }

                        int? hubPort = null;
                        var mHub = Regex.Match(cmd, @"--hub-port(?:=|\s+)(?:""(\d{1,5})""|'(\d{1,5})'|(\d{1,5}))");
                        if (mHub.Success)
                        {
                            string val = mHub.Groups[1].Value + mHub.Groups[2].Value + mHub.Groups[3].Value;
                            int parsed;
                            if (int.TryParse(val, out parsed)) hubPort = parsed;
                        }

                        var ports = new List<int>();
                        if (!string.IsNullOrEmpty(candCsrf))
                        {
                            ports = GetListeningPortsForPid(pid);
                        }
                        else if (hubPort.HasValue)
                        {
                            try
                            {
                                var req = (HttpWebRequest)WebRequest.Create(string.Format("http://127.0.0.1:{0}/", hubPort.Value));
                                req.Timeout = 1500;
                                using (var resp = req.GetResponse())
                                using (var reader = new StreamReader(resp.GetResponseStream()))
                                {
                                    string html = reader.ReadToEnd();
                                    var mCfg = Regex.Match(html, @"__APP_CONFIG__\s*=\s*(\{.*?\})\s*;");
                                    if (mCfg.Success)
                                    {
                                        var js = new JavaScriptSerializer();
                                        var cfg = js.Deserialize<Dictionary<string, object>>(mCfg.Groups[1].Value);
                                        if (cfg.ContainsKey("csrfToken") && cfg["csrfToken"] != null)
                                        {
                                            candCsrf = cfg["csrfToken"].ToString();
                                            ports.Add(hubPort.Value);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        if (!string.IsNullOrEmpty(candCsrf) && ports.Count > 0)
                        {
                            foreach (int port in ports.Distinct())
                            {
                                foreach (string proto in new string[] { "https", "http" })
                                {
                                    string testUrl = string.Format("{0}://127.0.0.1:{1}", proto, port);
                                    if (TestEndpoint(testUrl, candCsrf, out initialQuota))
                                    {
                                        baseUrl = testUrl;
                                        csrf = candCsrf;
                                        _cachedEndpoint = testUrl;
                                        _cachedCsrf = candCsrf;
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback to headless daemon
            return TryStartHeadlessDaemon(out baseUrl, out csrf, out initialQuota);
        }

        private static UsageData ParseLocalUsageData(string baseUrl, string csrf, Dictionary<string, object> quotaData)
        {
            if (quotaData == null)
            {
                try
                {
                    quotaData = InvokeRpc(baseUrl, "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary", csrf, null);
                }
                catch (Exception ex)
                {
                    var errResult = new UsageData();
                    errResult.IsConnected = false;
                    errResult.Error = "Failed to fetch quota: " + ex.Message;
                    errResult.LastUpdated = DateTime.Now;
                    return errResult;
                }
            }

            string userName = "User";
            string userEmail = "Local Antigravity";
            string planName = "Pro";

            try
            {
                var metaInner = new Dictionary<string, object>();
                metaInner.Add("ideName", "antigravity-ide");
                var metadata = new Dictionary<string, object>();
                metadata.Add("metadata", metaInner);

                var userStatus = InvokeRpc(baseUrl, "/exa.language_server_pb.LanguageServerService/GetUserStatus", csrf, metadata);
                if (userStatus != null && userStatus.ContainsKey("userStatus"))
                {
                    var us = userStatus["userStatus"] as Dictionary<string, object>;
                    if (us != null)
                    {
                        if (us.ContainsKey("name") && us["name"] != null) userName = us["name"].ToString();
                        if (us.ContainsKey("email") && us["email"] != null) userEmail = us["email"].ToString();
                        if (us.ContainsKey("planStatus") && us["planStatus"] is Dictionary<string, object>)
                        {
                            var ps = (Dictionary<string, object>)us["planStatus"];
                            if (ps.ContainsKey("planInfo") && ps["planInfo"] is Dictionary<string, object>)
                            {
                                var pi = (Dictionary<string, object>)ps["planInfo"];
                                if (pi.ContainsKey("planName") && pi["planName"] != null) planName = pi["planName"].ToString();
                            }
                        }
                    }
                }
            }
            catch { }

            var result = new UsageData();
            result.IsConnected = true;
            result.BaseUrl = baseUrl;
            result.UserName = userName;
            result.UserEmail = userEmail;
            result.Plan = planName;
            result.LastUpdated = DateTime.Now;

            if (quotaData != null && quotaData.ContainsKey("response"))
            {
                var resp = quotaData["response"] as Dictionary<string, object>;
                if (resp != null && resp.ContainsKey("groups") && resp["groups"] is ArrayList)
                {
                    var groups = (ArrayList)resp["groups"];
                    foreach (var grpObj in groups)
                    {
                        var grp = grpObj as Dictionary<string, object>;
                        if (grp == null) continue;

                        string grpName = grp.ContainsKey("displayName") && grp["displayName"] != null ? grp["displayName"].ToString().ToLower() : "";
                        bool isGemini = grpName.Contains("gemini") || grpName.Contains("flash");

                        if (grp.ContainsKey("buckets") && grp["buckets"] is ArrayList)
                        {
                            var buckets = (ArrayList)grp["buckets"];
                            foreach (var bObj in buckets)
                            {
                                var b = bObj as Dictionary<string, object>;
                                if (b == null) continue;

                                double rem = 1.0;
                                if (b.ContainsKey("remainingFraction") && b["remainingFraction"] != null)
                                {
                                    rem = Convert.ToDouble(b["remainingFraction"]);
                                }
                                double pct = Math.Round(rem * 100.0, 1);
                                string resetTime = b.ContainsKey("resetTime") && b["resetTime"] != null ? b["resetTime"].ToString() : null;
                                string resetStr = FormatRemainingTime(resetTime);

                                var model = new QuotaModel();
                                model.Pct = pct;
                                model.Remaining = rem;
                                model.ResetStr = resetStr;
                                model.ResetTime = resetTime;

                                string win = b.ContainsKey("window") && b["window"] != null ? b["window"].ToString().ToLower() : "";
                                if (isGemini)
                                {
                                    if (win == "5h") result.Gemini5h = model;
                                    else if (win == "weekly") result.GeminiWk = model;
                                }
                                else
                                {
                                    if (win == "5h") result.Claude5h = model;
                                    else if (win == "weekly") result.ClaudeWk = model;
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static UsageData GetUsageData(string targetSource = null)
        {
            if (string.IsNullOrEmpty(targetSource))
            {
                targetSource = AccountStorage.GetActiveAccount();
            }

            bool isExplicitLocal = string.Equals(targetSource, "local", StringComparison.OrdinalIgnoreCase);

            if (isExplicitLocal || string.IsNullOrEmpty(targetSource))
            {
                string baseUrl;
                string csrf;
                Dictionary<string, object> quotaData;

                if (FindConnection(out baseUrl, out csrf, out quotaData))
                {
                    var localData = ParseLocalUsageData(baseUrl, csrf, quotaData);
                    localData.SourceMode = "Local";
                    localData.ActiveAccount = "local";
                    localData.AvailableAccounts = AccountStorage.GetAccountEmails();
                    return localData;
                }

                // If local not found and not explicitly forced, try first available cloud account
                if (!isExplicitLocal)
                {
                    var accounts = AccountStorage.GetAccountEmails();
                    if (accounts.Count > 0)
                    {
                        targetSource = accounts[0];
                        AccountStorage.SetActiveAccount(targetSource);
                    }
                }
            }

            // Fetch from cloud if an account is selected
            if (!string.IsNullOrEmpty(targetSource) && !string.Equals(targetSource, "local", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var cloudData = CloudCodeClient.FetchUsage(targetSource);
                    return cloudData;
                }
                catch (Exception ex)
                {
                    var err = new UsageData();
                    err.IsConnected = false;
                    err.SourceMode = "Cloud";
                    err.ActiveAccount = targetSource;
                    err.UserEmail = targetSource;
                    err.UserName = targetSource.Contains("@") ? targetSource.Split('@')[0] : targetSource;
                    err.Error = "Cloud fetch failed: " + ex.Message;
                    err.LastUpdated = DateTime.Now;
                    err.AvailableAccounts = AccountStorage.GetAccountEmails();
                    return err;
                }
            }

            var errResult = new UsageData();
            errResult.IsConnected = false;
            errResult.SourceMode = "Local";
            errResult.ActiveAccount = "local";
            errResult.Error = "Antigravity language_server not found. Switch to Cloud mode or start Antigravity.";
            errResult.LastUpdated = DateTime.Now;
            errResult.AvailableAccounts = AccountStorage.GetAccountEmails();
            return errResult;
        }
    }

    public class StartupManager
    {
        private static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Antigravity Quota Monitor.lnk"); }
        }
        private const string RegRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegAppName = "AntigravityTrayMonitor";

        public static bool IsEnabled()
        {
            bool inFolder = File.Exists(ShortcutPath);
            bool inReg = false;
            using (var key = Registry.CurrentUser.OpenSubKey(RegRunKey, false))
            {
                if (key != null)
                {
                    inReg = key.GetValue(RegAppName) != null;
                }
            }
            return inFolder || inReg;
        }

        public static void SetEnabled(bool enable)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string workDir = Path.GetDirectoryName(exePath);

            if (enable)
            {
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                    shortcut.TargetPath = exePath;
                    shortcut.WorkingDirectory = workDir;
                    shortcut.Description = "Antigravity Quota Monitor";
                    shortcut.Save();
                }
                catch { }

                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RegRunKey, true))
                    {
                        if (key != null)
                        {
                            key.SetValue(RegAppName, string.Format("\"{0}\"", exePath));
                        }
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                }
                catch { }

                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RegRunKey, true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue(RegAppName, false);
                        }
                    }
                }
                catch { }
            }
        }
    }

    public class Program
    {
        private static void SafeLog(string msg)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
                File.AppendAllText(logPath, msg);
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                SafeLog("PROCESS EXIT TRIGGERED AT " + DateTime.Now.ToString("HH:mm:ss.fff") + "\r\nSTACK:\r\n" + Environment.StackTrace + "\r\n");
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                SafeLog("UNHANDLED EXCEPTION: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null") + "\r\n");
            };

            try
            {
                var curProc = Process.GetCurrentProcess();
                SafeLog(string.Format("Main started PID={0} Name={1} at {2}\r\n", curProc.Id, curProc.ProcessName, DateTime.Now.ToString("HH:mm:ss.fff")));

                bool createdNew;
                using (var mutex = new Mutex(true, "Local\\AntigravityQuotaMonitor_SingleInstanceMutex", out createdNew))
                {
                    SafeLog("Mutex createdNew: " + createdNew + "\r\n");
                    if (!createdNew)
                    {
                        SafeLog("Signaling existing instance...\r\n");
                        try
                        {
                            var ev = EventWaitHandle.OpenExisting("Local\\AntigravityQuotaMonitor_WakeupEvent");
                            ev.Set();
                        }
                        catch { }
                        return;
                    }

                    using (var wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AntigravityQuotaMonitor_WakeupEvent"))
                    {
                        SafeLog("Step 1: Creating Application...\r\n");
                        var app = new Application();
                        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                        app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                        {
                            SafeLog("DISPATCHER EXCEPTION: " + e.Exception.ToString() + "\r\n");
                            e.Handled = true;
                        };

                        SafeLog("Step 2: Creating DashboardWindow...\r\n");
                        var window = new DashboardWindow(wakeupEvent);

                        SafeLog("Step 3: window.Show()...\r\n");
                        window.Show();

                        SafeLog("Step 4: window.Activate()...\r\n");
                        window.Activate();

                        SafeLog("Step 5: Starting Application.Run()...\r\n");
                        app.Run();
                        SafeLog("Application.Run() ended.\r\n");
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog("MAIN EXCEPTION: " + ex.ToString() + "\r\n");
            }
        }
    }

    public class DashboardWindow : Window
    {
        private readonly EventWaitHandle _wakeupEvent;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _menuAccountsSub;
        private ToolStripMenuItem _menuStartup;
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _wakeupListener;

        private System.Windows.Controls.Image _imgAppIcon;
        private Ellipse _dotStatus;
        private TextBlock _txtStatus;
        private Button _btnRefresh;
        private Button _btnClose;

        private TextBlock _txtUserName;
        private Button _btnAccountSelect;
        private TextBlock _txtAccountIcon;
        private TextBlock _txtUserEmail;
        private Border _badgeSource;
        private TextBlock _txtSourceBadge;
        private Border _badgePlan;
        private TextBlock _txtPlanBadge;

        private TextBlock _txtGemini5hPct;
        private Border _barGemini5h;
        private TextBlock _txtGemini5hReset;
        private TextBlock _txtGeminiWkPct;
        private Border _barGeminiWk;
        private TextBlock _txtGeminiWkReset;

        private TextBlock _txtClaude5hPct;
        private Border _barClaude5h;
        private TextBlock _txtClaude5hReset;
        private TextBlock _txtClaudeWkPct;
        private Border _barClaudeWk;
        private TextBlock _txtClaudeWkReset;

        private TextBlock _txtLastUpdated;
        private CheckBox _chkAutoStart;

        private bool _isExiting = false;
        private string _currentActiveAccount = "local";
        private List<string> _availableAccounts = new List<string>();
        private const double TrackWidth = 304.0;

        public DashboardWindow(EventWaitHandle wakeupEvent)
        {
            _wakeupEvent = wakeupEvent;
            BuildUI();
            SetupTray();

            Loaded += delegate
            {
                PositionFlyout();
                UpdateData();
            };

            SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                PositionFlyout();
            };

            Deactivated += delegate
            {
                if (!_isExiting) Hide();
            };

            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!_isExiting)
                {
                    e.Cancel = true;
                    Hide();
                }
            };

            KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Hide();
            };

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(60);
            _refreshTimer.Tick += delegate { UpdateData(); };
            _refreshTimer.Start();

            _wakeupListener = new DispatcherTimer();
            _wakeupListener.Interval = TimeSpan.FromMilliseconds(500);
            _wakeupListener.Tick += delegate
            {
                if (_wakeupEvent != null && _wakeupEvent.WaitOne(0))
                {
                    ShowDashboard();
                }
            };
            _wakeupListener.Start();
        }

        private void PositionFlyout()
        {
            UpdateLayout();
            Rect workArea = SystemParameters.WorkArea;
            double w = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 384 : Width);
            double h = ActualHeight > 0 ? ActualHeight : 490;
            Left = Math.Max(workArea.Left, workArea.Right - w - 16);
            Top = Math.Max(workArea.Top, workArea.Bottom - h - 16);
        }

        public void ShowDashboard()
        {
            Show();
            PositionFlyout();
            Activate();
            Focus();
        }

        private SolidColorBrush Brush(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }

        private SolidColorBrush GetQuotaBrush(double rem)
        {
            if (rem > 0.5) return Brush("#10B981"); // green
            if (rem > 0.2) return Brush("#F59E0B"); // amber
            return Brush("#EF4444");                // red
        }

        private Style CreateHeaderButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Brush("#1E293B")));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, Brush("#334155")));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Brush("#94A3B8")));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 11.0));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 4, 10, 4)));

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "border");
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger();
            hoverTrigger.Property = Button.IsMouseOverProperty;
            hoverTrigger.Value = true;
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#334155"), "border"));
            hoverTrigger.Setters.Add(new Setter(Button.ForegroundProperty, Brush("#F8FAFC")));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            return style;
        }

        private Style CreateAccountButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Brush("#131C2E")));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, Brush("#334155")));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 6, 10, 6)));

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "border");
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger();
            hoverTrigger.Property = Button.IsMouseOverProperty;
            hoverTrigger.Value = true;
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#1E293B"), "border"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#475569"), "border"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            return style;
        }

        private Style CreateContextMenuStyle()
        {
            var style = new Style(typeof(System.Windows.Controls.ContextMenu));
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.OverridesDefaultStyleProperty, true));

            var template = new ControlTemplate(typeof(System.Windows.Controls.ContextMenu));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.BackgroundProperty, Brush("#0F172A"));
            borderFactory.SetValue(Border.BorderBrushProperty, Brush("#334155"));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.IsItemsHostProperty, true);
            borderFactory.AppendChild(panel);

            template.VisualTree = borderFactory;
            style.Setters.Add(new Setter(System.Windows.Controls.ContextMenu.TemplateProperty, template));
            return style;
        }

        private Style CreateMenuItemStyle()
        {
            var style = new Style(typeof(System.Windows.Controls.MenuItem));
            style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.ForegroundProperty, Brush("#E2E8F0")));
            style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.FontSizeProperty, 11.5));
            style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.CursorProperty, System.Windows.Input.Cursors.Hand));

            var template = new ControlTemplate(typeof(System.Windows.Controls.MenuItem));
            var border = new FrameworkElementFactory(typeof(Border), "itemBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.MarginProperty, new Thickness(2, 1, 2, 1));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, false);
            border.AppendChild(content);

            template.VisualTree = border;

            var highlightTrigger = new MultiTrigger();
            highlightTrigger.Conditions.Add(new Condition(System.Windows.Controls.MenuItem.IsHighlightedProperty, true));
            highlightTrigger.Conditions.Add(new Condition(System.Windows.Controls.MenuItem.IsEnabledProperty, true));
            highlightTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#1E293B"), "itemBorder"));
            template.Triggers.Add(highlightTrigger);

            var disabledTrigger = new Trigger();
            disabledTrigger.Property = System.Windows.Controls.MenuItem.IsEnabledProperty;
            disabledTrigger.Value = false;
            disabledTrigger.Setters.Add(new Setter(System.Windows.Controls.MenuItem.CursorProperty, System.Windows.Input.Cursors.Arrow));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.TemplateProperty, template));
            return style;
        }

        private Style CreateSeparatorStyle()
        {
            var style = new Style(typeof(System.Windows.Controls.Separator));
            style.Setters.Add(new Setter(System.Windows.Controls.Separator.OverridesDefaultStyleProperty, true));
            style.Setters.Add(new Setter(System.Windows.Controls.Separator.MarginProperty, new Thickness(4, 3, 4, 3)));

            var template = new ControlTemplate(typeof(System.Windows.Controls.Separator));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.HeightProperty, 1.0);
            border.SetValue(Border.BackgroundProperty, Brush("#334155"));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            template.VisualTree = border;
            style.Setters.Add(new Setter(System.Windows.Controls.Separator.TemplateProperty, template));
            return style;
        }

        private Border CreateQuotaCard(string title, string models,
            out TextBlock pct5h, out Border bar5h, out TextBlock reset5h,
            out TextBlock pctWk, out Border barWk, out TextBlock resetWk)
        {
            var card = new Border();
            card.Background = Brush("#1E293B");
            card.CornerRadius = new CornerRadius(8);
            card.BorderBrush = Brush("#334155");
            card.BorderThickness = new Thickness(1);
            card.Padding = new Thickness(14, 12, 14, 12);
            card.Margin = new Thickness(0, 0, 0, 10);

            var sp = new StackPanel();

            var headerGrid = new Grid();
            headerGrid.Margin = new Thickness(0, 0, 0, 8);

            var tbTitle = new TextBlock();
            tbTitle.Text = title;
            tbTitle.Foreground = Brush("#F8FAFC");
            tbTitle.FontSize = 12.5;
            tbTitle.FontWeight = FontWeights.SemiBold;
            headerGrid.Children.Add(tbTitle);

            var txtModels = new TextBlock();
            txtModels.Text = models;
            txtModels.Foreground = Brush("#64748B");
            txtModels.FontSize = 11;
            txtModels.HorizontalAlignment = HorizontalAlignment.Right;
            headerGrid.Children.Add(txtModels);
            sp.Children.Add(headerGrid);

            // 5-Hour Limit
            var g5h = new Grid();
            g5h.Margin = new Thickness(0, 0, 0, 4);
            var tb5h = new TextBlock();
            tb5h.Text = "5-Hour Limit";
            tb5h.Foreground = Brush("#CBD5E1");
            tb5h.FontSize = 11;
            g5h.Children.Add(tb5h);

            pct5h = new TextBlock();
            pct5h.Text = "100%";
            pct5h.Foreground = Brush("#10B981");
            pct5h.FontSize = 11.5;
            pct5h.FontWeight = FontWeights.Bold;
            pct5h.HorizontalAlignment = HorizontalAlignment.Right;
            g5h.Children.Add(pct5h);
            sp.Children.Add(g5h);

            var track5h = new Border();
            track5h.Height = 6;
            track5h.Background = Brush("#090D16");
            track5h.CornerRadius = new CornerRadius(3);
            track5h.Width = TrackWidth;
            track5h.HorizontalAlignment = HorizontalAlignment.Left;
            track5h.Margin = new Thickness(0, 0, 0, 4);

            bar5h = new Border();
            bar5h.HorizontalAlignment = HorizontalAlignment.Left;
            bar5h.Background = Brush("#10B981");
            bar5h.CornerRadius = new CornerRadius(3);
            bar5h.Width = 0;
            bar5h.MaxWidth = TrackWidth;
            track5h.Child = bar5h;
            sp.Children.Add(track5h);

            reset5h = new TextBlock();
            reset5h.Text = "Resets in ...";
            reset5h.Foreground = Brush("#64748B");
            reset5h.FontSize = 10.5;
            reset5h.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(reset5h);

            // Weekly Limit
            var gWk = new Grid();
            gWk.Margin = new Thickness(0, 0, 0, 4);
            var tbWk = new TextBlock();
            tbWk.Text = "Weekly Limit";
            tbWk.Foreground = Brush("#CBD5E1");
            tbWk.FontSize = 11;
            gWk.Children.Add(tbWk);

            pctWk = new TextBlock();
            pctWk.Text = "100%";
            pctWk.Foreground = Brush("#10B981");
            pctWk.FontSize = 11.5;
            pctWk.FontWeight = FontWeights.Bold;
            pctWk.HorizontalAlignment = HorizontalAlignment.Right;
            gWk.Children.Add(pctWk);
            sp.Children.Add(gWk);

            var trackWk = new Border();
            trackWk.Height = 6;
            trackWk.Background = Brush("#090D16");
            trackWk.CornerRadius = new CornerRadius(3);
            trackWk.Width = TrackWidth;
            trackWk.HorizontalAlignment = HorizontalAlignment.Left;
            trackWk.Margin = new Thickness(0, 0, 0, 4);

            barWk = new Border();
            barWk.HorizontalAlignment = HorizontalAlignment.Left;
            barWk.Background = Brush("#10B981");
            barWk.CornerRadius = new CornerRadius(3);
            barWk.Width = 0;
            barWk.MaxWidth = TrackWidth;
            trackWk.Child = barWk;
            sp.Children.Add(trackWk);

            resetWk = new TextBlock();
            resetWk.Text = "Resets in ...";
            resetWk.Foreground = Brush("#64748B");
            resetWk.FontSize = 10.5;
            sp.Children.Add(resetWk);

            card.Child = sp;
            return card;
        }

        private void BuildUI()
        {
            Title = "Antigravity Quota Monitor";
            Width = 384;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            var outerBorder = new Border();
            outerBorder.Margin = new Thickness(10);
            outerBorder.Background = Brush("#0F172A");
            outerBorder.CornerRadius = new CornerRadius(10);
            outerBorder.BorderBrush = Brush("#334155");
            outerBorder.BorderThickness = new Thickness(1);

            var shadow = new DropShadowEffect();
            shadow.BlurRadius = 24;
            shadow.Color = Colors.Black;
            shadow.Direction = 270;
            shadow.Opacity = 0.5;
            shadow.ShadowDepth = 6;
            outerBorder.Effect = shadow;

            var mainStack = new StackPanel();
            mainStack.Margin = new Thickness(16, 14, 16, 14);

            var headerBtnStyle = CreateHeaderButtonStyle();

            // SECTION 0: HEADER
            var headerGrid = new Grid();
            headerGrid.Margin = new Thickness(0, 0, 0, 12);
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _imgAppIcon = new System.Windows.Controls.Image();
            _imgAppIcon.Width = 30;
            _imgAppIcon.Height = 30;
            _imgAppIcon.Margin = new Thickness(0, 0, 10, 0);
            _imgAppIcon.VerticalAlignment = VerticalAlignment.Center;

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string pngPath = Path.Combine(exeDir, @"assets\icon.png");
            if (File.Exists(pngPath))
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage(new Uri(pngPath));
                _imgAppIcon.Source = bi;
            }
            Grid.SetColumn(_imgAppIcon, 0);
            headerGrid.Children.Add(_imgAppIcon);

            var titleSp = new StackPanel();
            titleSp.VerticalAlignment = VerticalAlignment.Center;
            var tbMainTitle = new TextBlock();
            tbMainTitle.Text = "Antigravity Quota";
            tbMainTitle.Foreground = Brush("#F8FAFC");
            tbMainTitle.FontSize = 13.5;
            tbMainTitle.FontWeight = FontWeights.SemiBold;
            titleSp.Children.Add(tbMainTitle);

            var statusSp = new StackPanel();
            statusSp.Orientation = System.Windows.Controls.Orientation.Horizontal;
            statusSp.Margin = new Thickness(0, 2, 0, 0);

            _dotStatus = new Ellipse();
            _dotStatus.Width = 7;
            _dotStatus.Height = 7;
            _dotStatus.Fill = Brush("#10B981");
            _dotStatus.VerticalAlignment = VerticalAlignment.Center;
            _dotStatus.Margin = new Thickness(0, 0, 6, 0);
            statusSp.Children.Add(_dotStatus);

            _txtStatus = new TextBlock();
            _txtStatus.Text = "Connected";
            _txtStatus.Foreground = Brush("#94A3B8");
            _txtStatus.FontSize = 11;
            _txtStatus.FontWeight = FontWeights.Medium;
            statusSp.Children.Add(_txtStatus);

            titleSp.Children.Add(statusSp);
            Grid.SetColumn(titleSp, 1);
            headerGrid.Children.Add(titleSp);

            var btnSp = new StackPanel();
            btnSp.Orientation = System.Windows.Controls.Orientation.Horizontal;
            btnSp.VerticalAlignment = VerticalAlignment.Center;

            _btnRefresh = new Button();
            _btnRefresh.Style = headerBtnStyle;
            _btnRefresh.Content = "Refresh";
            _btnRefresh.Margin = new Thickness(0, 0, 6, 0);
            _btnRefresh.Click += delegate { UpdateData(); };

            _btnClose = new Button();
            _btnClose.Style = headerBtnStyle;
            _btnClose.Content = "Close";
            _btnClose.Click += delegate { Hide(); };

            btnSp.Children.Add(_btnRefresh);
            btnSp.Children.Add(_btnClose);
            Grid.SetColumn(btnSp, 2);
            headerGrid.Children.Add(btnSp);

            mainStack.Children.Add(headerGrid);

            // SECTION 1: USER PROFILE & ACCOUNT SELECTOR
            var profileBorder = new Border();
            profileBorder.Background = Brush("#1E293B");
            profileBorder.CornerRadius = new CornerRadius(8);
            profileBorder.BorderBrush = Brush("#334155");
            profileBorder.BorderThickness = new Thickness(1);
            profileBorder.Padding = new Thickness(12, 10, 12, 10);
            profileBorder.Margin = new Thickness(0, 0, 0, 10);

            var profileStack = new StackPanel();

            // Top Row: User Display Name & Badges
            var profTopGrid = new Grid();
            profTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            profTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtUserName = new TextBlock();
            _txtUserName.Text = "User";
            _txtUserName.Foreground = Brush("#F8FAFC");
            _txtUserName.FontSize = 13.5;
            _txtUserName.FontWeight = FontWeights.SemiBold;
            _txtUserName.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_txtUserName, 0);
            profTopGrid.Children.Add(_txtUserName);

            // Badges Panel: [ SOURCE ] [ PLAN ]
            var badgesSp = new StackPanel();
            badgesSp.Orientation = System.Windows.Controls.Orientation.Horizontal;
            badgesSp.VerticalAlignment = VerticalAlignment.Center;

            _badgeSource = new Border();
            _badgeSource.Background = Brush("#1E293B");
            _badgeSource.BorderBrush = Brush("#475569");
            _badgeSource.BorderThickness = new Thickness(1);
            _badgeSource.CornerRadius = new CornerRadius(4);
            _badgeSource.Padding = new Thickness(6, 2, 6, 2);
            _badgeSource.Margin = new Thickness(0, 0, 6, 0);
            _badgeSource.VerticalAlignment = VerticalAlignment.Center;

            _txtSourceBadge = new TextBlock();
            _txtSourceBadge.Text = "LOCAL";
            _txtSourceBadge.Foreground = Brush("#94A3B8");
            _txtSourceBadge.FontSize = 9.5;
            _txtSourceBadge.FontWeight = FontWeights.SemiBold;
            _badgeSource.Child = _txtSourceBadge;
            badgesSp.Children.Add(_badgeSource);

            _badgePlan = new Border();
            _badgePlan.Background = Brush("#0F2F57");
            _badgePlan.BorderBrush = Brush("#1D4ED8");
            _badgePlan.BorderThickness = new Thickness(1);
            _badgePlan.CornerRadius = new CornerRadius(4);
            _badgePlan.Padding = new Thickness(6, 2, 6, 2);
            _badgePlan.VerticalAlignment = VerticalAlignment.Center;

            _txtPlanBadge = new TextBlock();
            _txtPlanBadge.Text = "PRO";
            _txtPlanBadge.Foreground = Brush("#93C5FD");
            _txtPlanBadge.FontSize = 9.5;
            _txtPlanBadge.FontWeight = FontWeights.SemiBold;
            _badgePlan.Child = _txtPlanBadge;
            badgesSp.Children.Add(_badgePlan);

            Grid.SetColumn(badgesSp, 1);
            profTopGrid.Children.Add(badgesSp);
            profileStack.Children.Add(profTopGrid);

            // Bottom Row: Dedicated Account Selector Bar
            _btnAccountSelect = new Button();
            _btnAccountSelect.Style = CreateAccountButtonStyle();
            _btnAccountSelect.Margin = new Thickness(0, 8, 0, 0);
            _btnAccountSelect.HorizontalAlignment = HorizontalAlignment.Stretch;
            _btnAccountSelect.Click += delegate { ShowAccountContextMenu(); };

            var accBtnGrid = new Grid();
            accBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            accBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            accBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtAccountIcon = new TextBlock();
            _txtAccountIcon.Text = "Account: ";
            _txtAccountIcon.FontSize = 11;
            _txtAccountIcon.Foreground = Brush("#64748B");
            _txtAccountIcon.FontWeight = FontWeights.Medium;
            _txtAccountIcon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_txtAccountIcon, 0);
            accBtnGrid.Children.Add(_txtAccountIcon);

            _txtUserEmail = new TextBlock();
            _txtUserEmail.Text = "Local Antigravity";
            _txtUserEmail.Foreground = Brush("#E2E8F0");
            _txtUserEmail.FontSize = 11;
            _txtUserEmail.VerticalAlignment = VerticalAlignment.Center;
            _txtUserEmail.TextTrimming = TextTrimming.CharacterEllipsis;
            _txtUserEmail.Margin = new Thickness(2, 0, 6, 0);
            Grid.SetColumn(_txtUserEmail, 1);
            accBtnGrid.Children.Add(_txtUserEmail);

            var arrow = new TextBlock();
            arrow.Text = "▾";
            arrow.Foreground = Brush("#64748B");
            arrow.FontSize = 10;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(arrow, 2);
            accBtnGrid.Children.Add(arrow);

            _btnAccountSelect.Content = accBtnGrid;
            profileStack.Children.Add(_btnAccountSelect);

            profileBorder.Child = profileStack;
            mainStack.Children.Add(profileBorder);

            // SECTION 2: QUOTA CARDS
            var cardsSp = new StackPanel();
            var geminiCard = CreateQuotaCard("Gemini Models", "Flash, Pro",
                out _txtGemini5hPct, out _barGemini5h, out _txtGemini5hReset,
                out _txtGeminiWkPct, out _barGeminiWk, out _txtGeminiWkReset);
            cardsSp.Children.Add(geminiCard);

            var claudeCard = CreateQuotaCard("Claude & GPT Models", "Sonnet, Opus, GPT",
                out _txtClaude5hPct, out _barClaude5h, out _txtClaude5hReset,
                out _txtClaudeWkPct, out _barClaudeWk, out _txtClaudeWkReset);
            cardsSp.Children.Add(claudeCard);

            mainStack.Children.Add(cardsSp);

            // SECTION 3: FOOTER
            var footerGrid = new Grid();
            footerGrid.Margin = new Thickness(0, 4, 0, 0);
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var footSp = new StackPanel();
            footSp.VerticalAlignment = VerticalAlignment.Center;

            _txtLastUpdated = new TextBlock();
            _txtLastUpdated.Text = "Updated: Just now";
            _txtLastUpdated.Foreground = Brush("#94A3B8");
            _txtLastUpdated.FontSize = 10.5;
            footSp.Children.Add(_txtLastUpdated);

            var txtInterval = new TextBlock();
            txtInterval.Text = "Auto-refresh: 60s";
            txtInterval.Foreground = Brush("#64748B");
            txtInterval.FontSize = 10;
            txtInterval.Margin = new Thickness(0, 1, 0, 0);
            footSp.Children.Add(txtInterval);

            Grid.SetColumn(footSp, 0);
            footerGrid.Children.Add(footSp);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.VerticalAlignment = VerticalAlignment.Center;
            _chkAutoStart.Foreground = Brush("#94A3B8");
            _chkAutoStart.FontSize = 11;
            _chkAutoStart.Content = "Start with Windows";
            _chkAutoStart.Cursor = System.Windows.Input.Cursors.Hand;
            _chkAutoStart.IsChecked = StartupManager.IsEnabled();

            _chkAutoStart.Click += delegate
            {
                bool target = _chkAutoStart.IsChecked == true;
                StartupManager.SetEnabled(target);
                if (_menuStartup != null) _menuStartup.Checked = target;
            };

            Grid.SetColumn(_chkAutoStart, 1);
            footerGrid.Children.Add(_chkAutoStart);

            mainStack.Children.Add(footerGrid);

            outerBorder.Child = mainStack;
            Content = outerBorder;
        }

        private void SetupTray()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string icoPath = Path.Combine(exeDir, @"assets\icon.ico");

            _notifyIcon = new NotifyIcon();
            if (File.Exists(icoPath))
            {
                _notifyIcon.Icon = new Icon(icoPath);
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
            _notifyIcon.Text = "Antigravity Quota Monitor";
            _notifyIcon.Visible = true;

            _contextMenu = new ContextMenuStrip();
            var menuOpen = _contextMenu.Items.Add("Open Dashboard");
            menuOpen.Font = new System.Drawing.Font(_contextMenu.Font, System.Drawing.FontStyle.Bold);
            menuOpen.Click += delegate { ShowDashboard(); };

            _contextMenu.Items.Add("-");

            _menuAccountsSub = new ToolStripMenuItem("Account / Source");
            _contextMenu.Items.Add(_menuAccountsSub);

            _contextMenu.Items.Add("-");

            var menuRefresh = _contextMenu.Items.Add("Refresh Usage");
            menuRefresh.Click += delegate { UpdateData(); };

            var menuInterval = new ToolStripMenuItem("Auto-Refresh Interval");
            var int30 = menuInterval.DropDownItems.Add("30 Seconds");
            var int60 = menuInterval.DropDownItems.Add("60 Seconds (Default)");
            var int300 = menuInterval.DropDownItems.Add("5 Minutes");
            ((ToolStripMenuItem)int60).Checked = true;

            int30.Click += delegate
            {
                ((ToolStripMenuItem)int30).Checked = true;
                ((ToolStripMenuItem)int60).Checked = false;
                ((ToolStripMenuItem)int300).Checked = false;
                _refreshTimer.Interval = TimeSpan.FromSeconds(30);
            };
            int60.Click += delegate
            {
                ((ToolStripMenuItem)int30).Checked = false;
                ((ToolStripMenuItem)int60).Checked = true;
                ((ToolStripMenuItem)int300).Checked = false;
                _refreshTimer.Interval = TimeSpan.FromSeconds(60);
            };
            int300.Click += delegate
            {
                ((ToolStripMenuItem)int30).Checked = false;
                ((ToolStripMenuItem)int60).Checked = false;
                ((ToolStripMenuItem)int300).Checked = true;
                _refreshTimer.Interval = TimeSpan.FromSeconds(300);
            };
            _contextMenu.Items.Add(menuInterval);

            _contextMenu.Items.Add("-");

            _menuStartup = new ToolStripMenuItem("Start with Windows");
            _menuStartup.Checked = StartupManager.IsEnabled();
            _menuStartup.Click += delegate
            {
                bool target = !_menuStartup.Checked;
                StartupManager.SetEnabled(target);
                _menuStartup.Checked = target;
                if (_chkAutoStart != null) _chkAutoStart.IsChecked = target;
            };
            _contextMenu.Items.Add(_menuStartup);

            _contextMenu.Items.Add("-");

            var menuExit = _contextMenu.Items.Add("Exit");
            menuExit.Click += delegate { ExitApplication(); };

            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.Click += delegate(object sender, EventArgs e)
            {
                var me = e as System.Windows.Forms.MouseEventArgs;
                if (me != null && me.Button == MouseButtons.Left)
                {
                    ShowDashboard();
                }
            };
            _notifyIcon.DoubleClick += delegate { ShowDashboard(); };
        }

        private void RefreshAccountsMenu(string active, List<string> accounts)
        {
            if (_menuAccountsSub == null) return;
            _menuAccountsSub.DropDownItems.Clear();

            bool isLocal = string.Equals(active, "local", StringComparison.OrdinalIgnoreCase);

            var itemLocal = new ToolStripMenuItem("Local IDE (language_server)");
            itemLocal.Checked = isLocal;
            itemLocal.Click += delegate { SwitchAccount("local"); };
            _menuAccountsSub.DropDownItems.Add(itemLocal);

            if (accounts != null && accounts.Count > 0)
            {
                _menuAccountsSub.DropDownItems.Add("-");
                foreach (string acc in accounts)
                {
                    string email = acc;
                    var itemAcc = new ToolStripMenuItem(email);
                    itemAcc.Checked = string.Equals(active, email, StringComparison.OrdinalIgnoreCase);
                    itemAcc.Click += delegate { SwitchAccount(email); };
                    _menuAccountsSub.DropDownItems.Add(itemAcc);
                }
            }

            _menuAccountsSub.DropDownItems.Add("-");
            var itemAdd = new ToolStripMenuItem("+ Add Google Account...");
            itemAdd.Click += delegate { StartAddAccount(); };
            _menuAccountsSub.DropDownItems.Add(itemAdd);
        }

        private void ShowAccountContextMenu()
        {
            var cm = new System.Windows.Controls.ContextMenu();
            cm.Style = CreateContextMenuStyle();
            cm.Resources.Add(typeof(System.Windows.Controls.MenuItem), CreateMenuItemStyle());
            cm.Resources.Add(typeof(System.Windows.Controls.Separator), CreateSeparatorStyle());
            cm.HasDropShadow = false;
            cm.MinWidth = _btnAccountSelect.ActualWidth > 0 ? Math.Max(_btnAccountSelect.ActualWidth, 260) : 260;

            var header = new System.Windows.Controls.MenuItem();
            header.Header = "SWITCH SOURCE / ACCOUNT";
            header.IsEnabled = false;
            header.Foreground = Brush("#64748B");
            header.FontSize = 9.5;
            header.FontWeight = FontWeights.Bold;
            cm.Items.Add(header);

            cm.Items.Add(new System.Windows.Controls.Separator());

            bool isLocal = string.Equals(_currentActiveAccount, "local", StringComparison.OrdinalIgnoreCase);

            var miLocal = new System.Windows.Controls.MenuItem();
            miLocal.Header = (isLocal ? "✓  " : "    ") + "Local IDE (language_server)";
            miLocal.Foreground = isLocal ? Brush("#38BDF8") : Brush("#E2E8F0");
            if (isLocal) miLocal.FontWeight = FontWeights.SemiBold;
            miLocal.Click += delegate { SwitchAccount("local"); };
            cm.Items.Add(miLocal);

            if (_availableAccounts != null && _availableAccounts.Count > 0)
            {
                cm.Items.Add(new System.Windows.Controls.Separator());
                foreach (string acc in _availableAccounts)
                {
                    string email = acc;
                    bool isActive = string.Equals(_currentActiveAccount, email, StringComparison.OrdinalIgnoreCase);
                    var miAcc = new System.Windows.Controls.MenuItem();
                    miAcc.Header = (isActive ? "✓  " : "    ") + email;
                    miAcc.Foreground = isActive ? Brush("#38BDF8") : Brush("#E2E8F0");
                    if (isActive) miAcc.FontWeight = FontWeights.SemiBold;
                    miAcc.Click += delegate { SwitchAccount(email); };
                    cm.Items.Add(miAcc);
                }
            }

            cm.Items.Add(new System.Windows.Controls.Separator());

            var miAdd = new System.Windows.Controls.MenuItem();
            miAdd.Header = "+  Add Google Account...";
            miAdd.Foreground = Brush("#38BDF8");
            miAdd.FontWeight = FontWeights.Medium;
            miAdd.Click += delegate { StartAddAccount(); };
            cm.Items.Add(miAdd);

            if (!isLocal && !string.IsNullOrEmpty(_currentActiveAccount))
            {
                var miRemove = new System.Windows.Controls.MenuItem();
                miRemove.Header = "✕  Remove Active Account";
                miRemove.Foreground = Brush("#F87171");
                miRemove.FontWeight = FontWeights.Medium;
                miRemove.Click += delegate { RemoveAccount(_currentActiveAccount); };
                cm.Items.Add(miRemove);
            }

            _btnAccountSelect.ContextMenu = cm;
            cm.PlacementTarget = _btnAccountSelect;
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            cm.VerticalOffset = 4;
            cm.IsOpen = true;
        }

        private void SwitchAccount(string account)
        {
            AccountStorage.SetActiveAccount(account);
            _dotStatus.Fill = Brush("#F59E0B");
            _txtStatus.Text = "Switching...";
            _txtStatus.Foreground = Brush("#F59E0B");
            UpdateData();
        }

        private void StartAddAccount()
        {
            _dotStatus.Fill = Brush("#3B82F6");
            _txtStatus.Text = "Connecting Google...";
            _txtStatus.Foreground = Brush("#3B82F6");

            GoogleOAuth.StartOAuthLogin(delegate(bool success, string msg)
            {
                Dispatcher.Invoke((Action)delegate
                {
                    if (success)
                    {
                        AccountStorage.SetActiveAccount(msg);
                        UpdateData();
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Failed to connect Google account: " + msg,
                            "Account Login Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        UpdateData();
                    }
                });
            });
        }

        private void RemoveAccount(string email)
        {
            var res = System.Windows.Forms.MessageBox.Show(
                string.Format("Are you sure you want to remove account '{0}'?", email),
                "Remove Account",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (res == System.Windows.Forms.DialogResult.Yes)
            {
                AccountStorage.RemoveAccount(email);
                UpdateData();
            }
        }

        private void ExitApplication()
        {
            _isExiting = true;
            try { _refreshTimer.Stop(); } catch { }
            try { _wakeupListener.Stop(); } catch { }
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            catch { }
            try { ApiClient.StopHeadlessDaemon(); } catch { }
            try { Close(); } catch { }
            try { Application.Current.Shutdown(); } catch { }
            Environment.Exit(0);
        }

        public void UpdateData()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var data = ApiClient.GetUsageData();

                    Dispatcher.Invoke((Action)delegate
                    {
                        try
                        {
                            _currentActiveAccount = data.ActiveAccount ?? "local";
                            _availableAccounts = data.AvailableAccounts ?? new List<string>();

                            RefreshAccountsMenu(_currentActiveAccount, _availableAccounts);

                            if (data.IsConnected)
                            {
                                _dotStatus.Fill = Brush("#10B981");
                                _txtStatus.Text = data.SourceMode == "Cloud" ? "Connected (Cloud)" : "Connected (Local)";
                                _txtStatus.Foreground = Brush("#10B981");

                                _txtUserName.Text = data.UserName;
                                _txtUserEmail.Text = data.UserEmail;
                                if (data.SourceMode == "Cloud")
                                {
                                    _badgeSource.Background = Brush("#2E1065");
                                    _badgeSource.BorderBrush = Brush("#6D28D9");
                                    _txtSourceBadge.Foreground = Brush("#DDD6FE");
                                    _txtSourceBadge.Text = "CLOUD";
                                    _txtAccountIcon.Text = "Account: ";
                                }
                                else
                                {
                                    _badgeSource.Background = Brush("#1E293B");
                                    _badgeSource.BorderBrush = Brush("#475569");
                                    _txtSourceBadge.Foreground = Brush("#94A3B8");
                                    _txtSourceBadge.Text = "LOCAL";
                                    _txtAccountIcon.Text = "Account: ";
                                }

                                _txtPlanBadge.Text = (data.Plan ?? "PRO").ToUpper();

                                // Gemini 5h
                                _txtGemini5hPct.Text = string.Format("{0}%", data.Gemini5h.Pct);
                                _txtGemini5hPct.Foreground = GetQuotaBrush(data.Gemini5h.Remaining);
                                _barGemini5h.Background = GetQuotaBrush(data.Gemini5h.Remaining);
                                _barGemini5h.Width = Math.Max(0, Math.Min(TrackWidth, TrackWidth * data.Gemini5h.Remaining));
                                _txtGemini5hReset.Text = string.Format("Resets in {0}", data.Gemini5h.ResetStr);

                                // Gemini Weekly
                                if (data.SourceMode == "Cloud")
                                {
                                    _txtGeminiWkPct.Text = "--";
                                    _txtGeminiWkPct.Foreground = Brush("#64748B");
                                    _barGeminiWk.Width = 0;
                                    _txtGeminiWkReset.Text = "Weekly pool only tracked in Local IDE";
                                }
                                else
                                {
                                    _txtGeminiWkPct.Text = string.Format("{0}%", data.GeminiWk.Pct);
                                    _txtGeminiWkPct.Foreground = GetQuotaBrush(data.GeminiWk.Remaining);
                                    _barGeminiWk.Background = GetQuotaBrush(data.GeminiWk.Remaining);
                                    _barGeminiWk.Width = Math.Max(0, Math.Min(TrackWidth, TrackWidth * data.GeminiWk.Remaining));
                                    _txtGeminiWkReset.Text = string.Format("Resets in {0}", data.GeminiWk.ResetStr);
                                }

                                // Claude 5h
                                _txtClaude5hPct.Text = string.Format("{0}%", data.Claude5h.Pct);
                                _txtClaude5hPct.Foreground = GetQuotaBrush(data.Claude5h.Remaining);
                                _barClaude5h.Background = GetQuotaBrush(data.Claude5h.Remaining);
                                _barClaude5h.Width = Math.Max(0, Math.Min(TrackWidth, TrackWidth * data.Claude5h.Remaining));
                                _txtClaude5hReset.Text = string.Format("Resets in {0}", data.Claude5h.ResetStr);

                                // Claude Weekly
                                if (data.SourceMode == "Cloud")
                                {
                                    _txtClaudeWkPct.Text = "--";
                                    _txtClaudeWkPct.Foreground = Brush("#64748B");
                                    _barClaudeWk.Width = 0;
                                    _txtClaudeWkReset.Text = "Weekly pool only tracked in Local IDE";
                                }
                                else
                                {
                                    _txtClaudeWkPct.Text = string.Format("{0}%", data.ClaudeWk.Pct);
                                    _txtClaudeWkPct.Foreground = GetQuotaBrush(data.ClaudeWk.Remaining);
                                    _barClaudeWk.Background = GetQuotaBrush(data.ClaudeWk.Remaining);
                                    _barClaudeWk.Width = Math.Max(0, Math.Min(TrackWidth, TrackWidth * data.ClaudeWk.Remaining));
                                    _txtClaudeWkReset.Text = string.Format("Resets in {0}", data.ClaudeWk.ResetStr);
                                }

                                string srcLabel = data.SourceMode == "Cloud" ? "Cloud" : "Local";
                                string tt = string.Format("Antigravity ({0})\nGemini: 5h {1}%\nClaude: 5h {2}%",
                                    srcLabel, data.Gemini5h.Pct, data.Claude5h.Pct);
                                if (tt.Length > 63) tt = tt.Substring(0, 63);
                                _notifyIcon.Text = tt;
                            }
                            else
                            {
                                _dotStatus.Fill = Brush("#EF4444");
                                _txtStatus.Text = "Disconnected";
                                _txtStatus.Foreground = Brush("#EF4444");
                                _txtUserEmail.Text = data.Error ?? "Connecting...";
                                _notifyIcon.Text = "Antigravity Quota\nConnecting...";
                            }

                            _txtLastUpdated.Text = string.Format("Updated: {0}", DateTime.Now.ToString("HH:mm:ss"));
                        }
                        catch (Exception exUi)
                        {
                            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
                            File.AppendAllText(logPath, "UpdateData UI EXCEPTION: " + exUi.ToString() + "\r\n");
                        }
                    });
                }
                catch (Exception exWorker)
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
                    File.AppendAllText(logPath, "UpdateData Worker EXCEPTION: " + exWorker.ToString() + "\r\n");
                }
            });
        }
    }
}
