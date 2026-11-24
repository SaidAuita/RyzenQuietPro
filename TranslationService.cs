using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    public enum TranslationStatusKind
    {
        Ready,
        Translating,
        OcrScanning,
        FreeingVram,
        VramFreed,
        Error
    }

    public class TranslationService : IDisposable
    {
        private static TranslationService? _instance;
        public static TranslationService Instance => _instance ??= new TranslationService();

        private readonly HttpClient _httpClient;
        private static readonly HttpClient _queryClient = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly SemaphoreSlim _modelLoadLock = new(1, 1);
        private AppSettings _settings;
        private System.Threading.Timer? _vramTimer;
        private readonly object _lock = new();

        public TranslationStatusKind StatusKind { get; private set; } = TranslationStatusKind.Ready;
        private string? _customStatusKey;

        public string CurrentStatus => GetLocalizedStatus();

        public string GetLocalizedStatus()
        {
            return StatusKind switch
            {
                TranslationStatusKind.Ready => Loc.Get("Translate_Ready"),
                TranslationStatusKind.Translating => Loc.Get("Translate_Translating"),
                TranslationStatusKind.OcrScanning => Loc.Get("Translate_OcrScanning"),
                TranslationStatusKind.FreeingVram => Loc.Get("Translate_FreeingVram"),
                TranslationStatusKind.VramFreed => Loc.Get("Translate_VramFreed"),
                _ => !string.IsNullOrEmpty(_customStatusKey) ? Loc.Get(_customStatusKey) : Loc.Get("Translate_Ready")
            };
        }

        public bool IsVramLoaded { get; private set; } = false;
        public DateTime? LastRequestTime { get; private set; }
        public DateTime? AutoUnloadTime { get; private set; }

        public event Action<string>? StatusChanged;
        public event Action? VramStateChanged;

        public static readonly (string Code, string EnName, string LocalName)[] SupportedLanguages = new[]
        {
            ("ru", "Russian", "Русский"),
            ("en", "English", "English"),
            ("de", "German", "Deutsch"),
            ("es", "Spanish", "Español"),
            ("fr", "French", "Français"),
            ("it", "Italian", "Italiano"),
            ("zh", "Chinese", "中文"),
            ("ja", "Japanese", "日本語"),
            ("uk", "Ukrainian", "Українська"),
            ("tr", "Turkish", "Türkçe"),
            ("pl", "Polish", "Polski"),
            ("pt", "Portuguese", "Português"),
            ("ko", "Korean", "한국어"),
            ("ar", "Arabic", "العربية")
        };

        public TranslationService(AppSettings? settings = null)
        {
            _settings = settings ?? AppSettings.Load();
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            _instance = this;
        }

        public void UpdateSettings(AppSettings settings)
        {
            _settings = settings;
            ResetVramTimer();
        }

        private void SetStatus(TranslationStatusKind kind, string? customKey = null)
        {
            StatusKind = kind;
            _customStatusKey = customKey;
            StatusChanged?.Invoke(GetLocalizedStatus());
        }

        #region Connection and Model Discovery

        public async Task<(bool isOnline, List<string> models, string activeModel, string statusMessage)> CheckConnectionAndGetModelsAsync()
        {
            string baseUrl = (_settings.TranslateApiUrl ?? "http://127.0.0.1:1234/v1").TrimEnd('/');
            var modelsList = new List<string>();
            string activeModel = _settings.TranslateModel ?? "";

            // A. LM STUDIO BACKEND
            if (baseUrl.Contains(":1234"))
            {
                // 1. If server is online, query HTTP endpoint
                if (IsPortListening("127.0.0.1", 1234, 150))
                {
                    modelsList = await FetchOpenAiModelsHttpAsync(baseUrl);
                    if (modelsList.Count > 0)
                    {
                        if (string.IsNullOrEmpty(activeModel) || !modelsList.Contains(activeModel))
                            activeModel = modelsList[0];
                        return (true, modelsList, activeModel, $"OK: {modelsList.Count} model(s)");
                    }
                }

                // 2. Offline discovery: scan via 'lms ls --json' or ~/.lmstudio/models
                var lmsOffline = GetLmStudioOfflineModels();
                if (lmsOffline.Count > 0)
                {
                    // Asynchronously wake up LM Studio server in the background
                    _ = Task.Run(EnsureLmsServerRunningAsync);

                    if (string.IsNullOrEmpty(activeModel) || !lmsOffline.Contains(activeModel))
                        activeModel = lmsOffline[0];

                    return (true, lmsOffline, activeModel, $"OK (Offline): {lmsOffline.Count} model(s) found");
                }

                return (false, modelsList, activeModel, "LM Studio offline (порт 1234). Запустите LM Studio.");
            }

            // B. OLLAMA BACKEND
            if (baseUrl.Contains(":11434"))
            {
                // 1. If server is online, query HTTP endpoint
                if (IsPortListening("127.0.0.1", 11434, 150))
                {
                    modelsList = await FetchOllamaModelsHttpAsync();
                    if (modelsList.Count == 0)
                    {
                        modelsList = await FetchOpenAiModelsHttpAsync(baseUrl);
                    }

                    if (modelsList.Count > 0)
                    {
                        if (string.IsNullOrEmpty(activeModel) || !modelsList.Contains(activeModel))
                            activeModel = modelsList[0];
                        return (true, modelsList, activeModel, $"OK: {modelsList.Count} model(s)");
                    }
                }

                // 2. Offline discovery: scan via manifests or CLI 'ollama list'
                var ollamaOffline = GetOllamaOfflineModels();
                if (ollamaOffline.Count > 0)
                {
                    // Asynchronously wake up Ollama server in the background
                    _ = Task.Run(EnsureOllamaServerRunningAsync);

                    if (string.IsNullOrEmpty(activeModel) || !ollamaOffline.Contains(activeModel))
                        activeModel = ollamaOffline[0];

                    return (true, ollamaOffline, activeModel, $"OK (Offline): {ollamaOffline.Count} model(s) found");
                }

                return (false, modelsList, activeModel, "Ollama offline (порт 11434). Запустите Ollama.");
            }

            // C. CUSTOM / GENERIC OPENAI ENDPOINT
            modelsList = await FetchOpenAiModelsHttpAsync(baseUrl);
            if (modelsList.Count > 0)
            {
                if (string.IsNullOrEmpty(activeModel) || !modelsList.Contains(activeModel))
                    activeModel = modelsList[0];
                return (true, modelsList, activeModel, $"OK: {modelsList.Count} model(s)");
            }

            return (false, modelsList, activeModel, "Сервер недоступен (Connection failed)");
        }

        private async Task<List<string>> FetchOpenAiModelsHttpAsync(string baseUrl)
        {
            var list = new List<string>();
            try
            {
                string modelsUrl = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{baseUrl}/models"
                    : $"{baseUrl}/v1/models";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await _httpClient.GetAsync(modelsUrl, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in dataElem.EnumerateArray())
                        {
                            if (m.TryGetProperty("id", out var idElem))
                            {
                                string id = idElem.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(id) && !list.Contains(id))
                                    list.Add(id);
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private async Task<List<string>> FetchOllamaModelsHttpAsync()
        {
            var list = new List<string>();
            try
            {
                string ollamaUrl = "http://127.0.0.1:11434/api/tags";
                using var ctsOllama = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var responseOllama = await _httpClient.GetAsync(ollamaUrl, ctsOllama.Token);

                if (responseOllama.IsSuccessStatusCode)
                {
                    string json = await responseOllama.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in modelsElem.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var nameElem))
                            {
                                string name = nameElem.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name))
                                    list.Add(name);
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        #region Local Model Discovery & Server Control Helpers

        public static List<string> GetLmStudioOfflineModels()
        {
            var list = new List<string>();

            // 1. Try 'lms ls --json' CLI - works even when server is off
            string? lms = FindLmsCli();
            if (!string.IsNullOrEmpty(lms))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = lms,
                        Arguments = "ls --json",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        string json = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);
                        if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith("["))
                        {
                            using var doc = JsonDocument.Parse(json);
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var typeElem) &&
                                    typeElem.GetString() == "embedding")
                                    continue; // Skip embedding models

                                string key = "";
                                if (item.TryGetProperty("modelKey", out var mkElem))
                                    key = mkElem.GetString() ?? "";
                                else if (item.TryGetProperty("path", out var pElem))
                                    key = pElem.GetString() ?? "";

                                if (!string.IsNullOrWhiteSpace(key) && !list.Contains(key))
                                {
                                    list.Add(key);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Translation] lms ls --json error: {ex.Message}");
                }
            }

            if (list.Count > 0) return list;

            // 2. Direct folder scan fallback
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string modelsDir = Path.Combine(home, ".lmstudio", "models");
                if (Directory.Exists(modelsDir))
                {
                    var files = Directory.GetFiles(modelsDir, "*.gguf", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        string lower = fileName.ToLowerInvariant();
                        if (lower.StartsWith("mmproj-") || lower.StartsWith("mtp-") || lower.Contains("embed"))
                            continue;

                        string relPath = Path.GetRelativePath(modelsDir, file).Replace('\\', '/');
                        if (!list.Contains(relPath))
                            list.Add(relPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Translation] Error scanning LM Studio models: {ex.Message}");
            }
            return list;
        }

        public static List<string> GetOllamaOfflineModels()
        {
            var list = new List<string>();

            // 1. Try 'ollama list' CLI
            string? ollamaCli = FindOllamaCli();
            if (!string.IsNullOrEmpty(ollamaCli))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ollamaCli,
                        Arguments = "list",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string outText = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(2000);
                        var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                            {
                                if (!list.Contains(parts[0]))
                                    list.Add(parts[0]);
                            }
                        }
                    }
                }
                catch { }
            }

            if (list.Count > 0) return list;

            // 2. Direct disk scan of Ollama manifests directory (works 100% offline without service)
            try
            {
                var searchRoots = new List<string>();
                string? envModels = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
                if (!string.IsNullOrWhiteSpace(envModels) && Directory.Exists(envModels))
                {
                    searchRoots.Add(Path.Combine(envModels, "manifests"));
                }

                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                searchRoots.Add(Path.Combine(home, ".ollama", "models", "manifests"));

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                searchRoots.Add(Path.Combine(localApp, "Programs", "Ollama", "models", "manifests"));

                foreach (var root in searchRoots)
                {
                    if (!Directory.Exists(root)) continue;

                    var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // Path format: <registry>/<user_or_library>/<model>/<tag>
                        string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                        var parts = rel.Split('/');
                        if (parts.Length >= 3)
                        {
                            string org = parts[1];
                            string name = parts[2];
                            string tag = parts.Length >= 4 ? parts[3] : "latest";

                            string modelId = org.Equals("library", StringComparison.OrdinalIgnoreCase)
                                ? $"{name}:{tag}"
                                : $"{org}/{name}:{tag}";

                            if (!list.Contains(modelId))
                            {
                                list.Add(modelId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Translation] Error scanning Ollama manifests: {ex.Message}");
            }
            return list;
        }

        public static string? FindLmsCli()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = new[]
            {
                Path.Combine(home, ".lmstudio", "bin", "lms.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LM Studio", "resources", "app", ".webpack", "bin", "lms.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LM Studio", "resources", "app", ".webpack", "bin", "lms.exe")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(p.Trim(), "lms.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static string? FindOllamaCli()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cand = Path.Combine(localApp, "Programs", "Ollama", "ollama.exe");
            if (File.Exists(cand)) return cand;

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            cand = Path.Combine(progFiles, "Ollama", "ollama.exe");
            if (File.Exists(cand)) return cand;

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(p.Trim(), "ollama.exe");
                if (File.Exists(candidate)) return candidate;
            }

            try
            {
                var psi = new ProcessStartInfo("where", "ollama")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string outStr = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(1000);
                    if (!string.IsNullOrEmpty(outStr) && File.Exists(outStr.Split('\n')[0].Trim()))
                        return outStr.Split('\n')[0].Trim();
                }
            }
            catch { }

            return null;
        }

        public static bool IsPortListening(string host, int port, int timeoutMs = 150)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var result = tcp.BeginConnect(host, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeoutMs);
                if (!success) return false;
                tcp.EndConnect(result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> EnsureLmsServerRunningAsync()
        {
            if (IsPortListening("127.0.0.1", 1234, 150))
                return true;

            string? lms = FindLmsCli();
            if (string.IsNullOrEmpty(lms)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = lms,
                    Arguments = "server start",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(200);
                    if (IsPortListening("127.0.0.1", 1234, 150))
                        return true;
                }
            }
            catch { }
            return IsPortListening("127.0.0.1", 1234, 150);
        }

        public static async Task<bool> EnsureOllamaServerRunningAsync()
        {
            if (IsPortListening("127.0.0.1", 11434, 150))
                return true;

            string? ollama = FindOllamaCli();
            if (string.IsNullOrEmpty(ollama)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ollama,
                    Arguments = "serve",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(psi);
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(200);
                    if (IsPortListening("127.0.0.1", 11434, 150))
                        return true;
                }
            }
            catch { }
            return IsPortListening("127.0.0.1", 11434, 150);
        }

        public static async Task EnsureBackendReadyAsync(string baseUrl, string? model = null)
        {
            if (baseUrl.Contains(":1234"))
            {
                if (!IsPortListening("127.0.0.1", 1234, 150))
                {
                    await EnsureLmsServerRunningAsync();
                }

                if (!string.IsNullOrWhiteSpace(model) && model != "default" && model != "local-model")
                {
                    await EnsureSingleLmsModelLoadedAsync(model);
                }
            }
            else if (baseUrl.Contains(":11434"))
            {
                if (!IsPortListening("127.0.0.1", 11434, 150))
                {
                    await EnsureOllamaServerRunningAsync();
                }

                if (!string.IsNullOrWhiteSpace(model) && model != "default" && model != "local-model")
                {
                    await EnsureSingleOllamaModelLoadedAsync(model);
                }
            }
        }

        public static async Task<bool> LoadLmsModelAsync(string modelKey)
        {
            return await EnsureSingleLmsModelLoadedAsync(modelKey);
        }

        public static async Task<bool> EnsureSingleLmsModelLoadedAsync(string modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey) || modelKey == "default" || modelKey == "local-model")
                return true;

            await _modelLoadLock.WaitAsync();
            try
            {
                if (!IsPortListening("127.0.0.1", 1234, 150))
                {
                    await EnsureLmsServerRunningAsync();
                }

                var loadedModels = await GetLoadedLmsModelsAsync();

                // 1. If exactly 1 model is loaded and it matches our desired model, we're ready!
                if (loadedModels.Count == 1 && IsModelMatch(loadedModels[0], modelKey))
                {
                    Logger.Log($"[Translation] Model '{modelKey}' is already active in LM Studio VRAM. Skipping redundant load.");
                    return true;
                }

                // 2. If any other model is loaded or duplicate instances exist (>1), unload all first to enforce single model rule
                if (loadedModels.Count > 0)
                {
                    Logger.Log($"[Translation] LM Studio has {loadedModels.Count} model(s) in VRAM ({string.Join(", ", loadedModels)}). Unloading to ensure single model...");
                    await UnloadAllLmsModelsInternalAsync();
                }

                // 3. Load the single desired model
                Logger.Log($"[Translation] Loading model '{modelKey}' into VRAM (single instance)...");
                return await LoadLmsModelInternalAsync(modelKey);
            }
            finally
            {
                _modelLoadLock.Release();
            }
        }

        public static async Task<List<string>> GetLoadedLmsModelsAsync()
        {
            var loaded = new List<string>();

            // Method A: Query LM Studio REST API GET /api/v0/models (fast, ~2-5 ms)
            if (IsPortListening("127.0.0.1", 1234, 100))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                    var resp = await _queryClient.GetAsync("http://127.0.0.1:1234/api/v0/models", cts.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(cts.Token);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in dataElem.EnumerateArray())
                            {
                                if (item.TryGetProperty("state", out var stateElem) &&
                                    string.Equals(stateElem.GetString(), "loaded", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (item.TryGetProperty("id", out var idElem))
                                    {
                                        string id = idElem.GetString() ?? "";
                                        if (!string.IsNullOrEmpty(id)) loaded.Add(id);
                                    }
                                }
                            }
                        }
                        return loaded;
                    }
                }
                catch { }
            }

            // Method B: Fallback via CLI 'lms ps --json'
            string? lms = FindLmsCli();
            if (!string.IsNullOrEmpty(lms))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = lms,
                        Arguments = "ps --json",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = await proc.StandardOutput.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        if (!string.IsNullOrWhiteSpace(output) && output.TrimStart().StartsWith("["))
                        {
                            using var doc = JsonDocument.Parse(output);
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                string id = "";
                                if (item.TryGetProperty("identifier", out var idElem)) id = idElem.GetString() ?? "";
                                else if (item.TryGetProperty("modelKey", out var mkElem)) id = mkElem.GetString() ?? "";

                                if (!string.IsNullOrEmpty(id)) loaded.Add(id);
                            }
                        }
                    }
                }
                catch { }
            }

            return loaded;
        }

        public static async Task<bool> EnsureSingleOllamaModelLoadedAsync(string modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey) || modelKey == "default" || modelKey == "local-model")
                return true;

            await _modelLoadLock.WaitAsync();
            try
            {
                if (!IsPortListening("127.0.0.1", 11434, 150))
                {
                    await EnsureOllamaServerRunningAsync();
                }

                // Check loaded models via /api/ps
                var loaded = new List<string>();
                try
                {
                    using var ctsPs = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var psResp = await _queryClient.GetStringAsync("http://127.0.0.1:11434/api/ps", ctsPs.Token);
                    using var doc = JsonDocument.Parse(psResp);
                    if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in modelsElem.EnumerateArray())
                        {
                            string mName = "";
                            if (m.TryGetProperty("name", out var n)) mName = n.GetString() ?? "";
                            else if (m.TryGetProperty("model", out var mn)) mName = mn.GetString() ?? "";
                            if (!string.IsNullOrEmpty(mName)) loaded.Add(mName);
                        }
                    }
                }
                catch { }

                // Unload any different models from Ollama to ensure strictly 1 model in memory
                foreach (var m in loaded)
                {
                    if (!IsModelMatch(m, modelKey))
                    {
                        try
                        {
                            var payload = new { model = m, keep_alive = 0 };
                            string jsonPayload = JsonSerializer.Serialize(payload);
                            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await _queryClient.PostAsync("http://127.0.0.1:11434/api/generate", content, cts.Token);
                            Logger.Log($"[Translation] Ollama: unloaded prior model '{m}' to keep strictly 1 model in VRAM.");
                        }
                        catch { }
                    }
                }

                return true;
            }
            finally
            {
                _modelLoadLock.Release();
            }
        }

        private static bool IsModelMatch(string loaded, string requested)
        {
            if (string.Equals(loaded, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            // Strip worker suffix if any, e.g. "gemma-4-e4b-it:2" -> "gemma-4-e4b-it"
            string loadedBase = loaded;
            int colonIdx = loaded.LastIndexOf(':');
            if (colonIdx > 0 && int.TryParse(loaded.Substring(colonIdx + 1), out _))
            {
                loadedBase = loaded.Substring(0, colonIdx);
            }

            if (string.Equals(loadedBase, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check without organization prefix, e.g. "google/gemma-4-e2b" vs "gemma-4-e2b"
            string requestedBase = requested.Contains('/') ? requested.Substring(requested.LastIndexOf('/') + 1) : requested;
            string loadedBaseNoOrg = loadedBase.Contains('/') ? loadedBase.Substring(loadedBase.LastIndexOf('/') + 1) : loadedBase;

            return string.Equals(loadedBaseNoOrg, requestedBase, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<bool> UnloadAllLmsModelsInternalAsync()
        {
            string? lms = FindLmsCli();
            if (string.IsNullOrEmpty(lms)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = lms,
                    Arguments = "unload --all",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return proc.ExitCode == 0;
                }
            }
            catch { }
            return false;
        }

        private static async Task<bool> LoadLmsModelInternalAsync(string modelKey)
        {
            string? lms = FindLmsCli();
            if (string.IsNullOrEmpty(lms)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = lms,
                    Arguments = $"load -y \"{modelKey}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return proc.ExitCode == 0;
                }
            }
            catch { }
            return false;
        }

        #endregion

        #endregion

        #region Text Translation

        public async Task<(string result, double latencyMs, string modelUsed)> TranslateTextAsync(
            string text,
            string? targetLang = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (string.Empty, 0, string.Empty);

            string cleanText = text.Trim();
            string tgtLangCode = targetLang ?? _settings.TranslateTargetLang ?? "ru";
            var (srcName, tgtName) = ResolveLanguageNames(cleanText, tgtLangCode);

            string baseUrl = (_settings.TranslateApiUrl ?? "http://127.0.0.1:1234/v1").TrimEnd('/');
            string endpoint = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/chat/completions"
                : $"{baseUrl}/v1/chat/completions";

            string modelToUse = !string.IsNullOrWhiteSpace(_settings.TranslateModel)
                ? _settings.TranslateModel
                : "local-model";

            string systemPrompt =
                $"You are a professional translator. Translate the text from {srcName} to {tgtName}.\n" +
                "Requirements:\n" +
                "- Output ONLY the direct translation.\n" +
                "- Do NOT include <think> tags, explanations, notes, greetings, or conversational filler.\n" +
                "- Preserve original formatting, bullet points, code blocks, and technical terms.";

            string userContent = $"Translate the following text to {tgtName}:\n\n{cleanText}";

            int estimatedTokens = Math.Max(2048, Math.Min(16384, cleanText.Length * 2 + 1024));

            var payload = new
            {
                model = modelToUse,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.2,
                max_tokens = estimatedTokens,
                stream = false
            };

            SetStatus(TranslationStatusKind.Translating);
            var sw = Stopwatch.StartNew();

            try
            {
                await EnsureBackendReadyAsync(baseUrl, modelToUse);

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errBody}");
                }

                string respJson = await response.Content.ReadAsStringAsync();
                string outputText = ExtractTextFromChatResponse(respJson, out string returnedModel);
                if (string.IsNullOrEmpty(returnedModel)) returnedModel = modelToUse;

                IsVramLoaded = true;
                LastRequestTime = DateTime.Now;
                ResetVramTimer();
                SetStatus(TranslationStatusKind.Ready);
                VramStateChanged?.Invoke();

                return (outputText, latency, returnedModel);
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetStatus(TranslationStatusKind.Error, "Translate_Error");
                Logger.Log($"[Translation] TranslateTextAsync error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Screen OCR and Vision Recognition

        public async Task<(string result, double latencyMs, string modelUsed)> RecognizeImageAsync(
            byte[] pngBytes,
            bool andTranslate = false,
            string? targetLang = null)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                return (string.Empty, 0, string.Empty);

            string base64Image = Convert.ToBase64String(pngBytes);
            string tgtLangCode = targetLang ?? _settings.TranslateTargetLang ?? "ru";
            var (_, tgtName) = ResolveLanguageNames(string.Empty, tgtLangCode);

            string prompt = andTranslate
                ? $"Transcribe all text from this image and translate it to {tgtName}. Output ONLY the direct translated text, preserving line breaks and original structure. Do NOT include <think> tags, explanations, notes, or markdown code blocks."
                : "Transcribe all text from this image accurately and completely. Output ONLY the extracted text, exactly as shown, preserving line breaks. Do NOT add any introduction, explanations, comments, or markdown code blocks.";

            string baseUrl = (_settings.TranslateApiUrl ?? "http://127.0.0.1:1234/v1").TrimEnd('/');
            string endpoint = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/chat/completions"
                : $"{baseUrl}/v1/chat/completions";

            string modelToUse = !string.IsNullOrWhiteSpace(_settings.TranslateModel)
                ? _settings.TranslateModel
                : "local-model";

            var payload = new
            {
                model = modelToUse,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/png;base64,{base64Image}"
                                }
                            }
                        }
                    }
                },
                temperature = 0.1,
                max_tokens = 2048,
                stream = false
            };

            SetStatus(TranslationStatusKind.OcrScanning);
            var sw = Stopwatch.StartNew();

            try
            {
                await EnsureBackendReadyAsync(baseUrl, modelToUse);

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errBody}");
                }

                string respJson = await response.Content.ReadAsStringAsync();
                string outputText = ExtractTextFromChatResponse(respJson, out string returnedModel);
                if (string.IsNullOrEmpty(returnedModel)) returnedModel = modelToUse;

                IsVramLoaded = true;
                LastRequestTime = DateTime.Now;
                ResetVramTimer();
                SetStatus(TranslationStatusKind.Ready);
                VramStateChanged?.Invoke();

                return (outputText, latency, returnedModel);
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetStatus(TranslationStatusKind.Error, "Translate_ErrorOcr");
                Logger.Log($"[Translation] RecognizeImageAsync error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region VRAM Freeing and Timer

        public void ResetVramTimer()
        {
            lock (_lock)
            {
                _vramTimer?.Dispose();
                _vramTimer = null;
                AutoUnloadTime = null;

                if (_settings.TranslateVramTimeoutMinutes > 0 && IsVramLoaded)
                {
                    int delayMs = _settings.TranslateVramTimeoutMinutes * 60 * 1000;
                    AutoUnloadTime = DateTime.Now.AddMilliseconds(delayMs);
                    _vramTimer = new System.Threading.Timer(OnVramTimerFired, null, delayMs, Timeout.Infinite);
                }
            }
            VramStateChanged?.Invoke();
        }

        private async void OnVramTimerFired(object? state)
        {
            Logger.Log($"[Translation] Inactivity timer ({_settings.TranslateVramTimeoutMinutes}m) triggered. Unloading model from VRAM...");
            await FreeVramAsync();
        }

        public async Task<(bool success, string message)> FreeVramAsync()
        {
            await _modelLoadLock.WaitAsync();
            try
            {
                SetStatus(TranslationStatusKind.FreeingVram);
                bool unloadedAny = false;
                var details = new StringBuilder();

                string modelToUnload = _settings.TranslateModel ?? "";

            // 1. Ollama: query /api/ps and unload all loaded models via keep_alive = 0
            try
            {
                using var ctsPs = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var psResp = await _httpClient.GetStringAsync("http://127.0.0.1:11434/api/ps", ctsPs.Token);
                using var doc = JsonDocument.Parse(psResp);
                if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modelsElem.EnumerateArray())
                    {
                        string modelName = "";
                        if (m.TryGetProperty("name", out var n)) modelName = n.GetString() ?? "";
                        else if (m.TryGetProperty("model", out var mn)) modelName = mn.GetString() ?? "";

                        if (!string.IsNullOrEmpty(modelName))
                        {
                            var payload = new { model = modelName, keep_alive = 0 };
                            string jsonPayload = JsonSerializer.Serialize(payload);
                            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            var resp = await _httpClient.PostAsync("http://127.0.0.1:11434/api/generate", content, cts.Token);
                            if (resp.IsSuccessStatusCode) unloadedAny = true;
                        }
                    }
                }
            }
            catch { }

            // 1b. Fallback for specific model in Ollama
            if (!string.IsNullOrEmpty(modelToUnload))
            {
                try
                {
                    string ollamaUrl = "http://127.0.0.1:11434/api/generate";
                    var payload = new
                    {
                        model = modelToUnload,
                        keep_alive = 0
                    };
                    string jsonPayload = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var resp = await _httpClient.PostAsync(ollamaUrl, content, cts.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        unloadedAny = true;
                        details.Append("Ollama: unloaded. ");
                    }
                }
                catch { }
            }

            // 1c. Ollama CLI stop
            string? ollamaCli = FindOllamaCli();
            if (ollamaCli != null && !string.IsNullOrEmpty(modelToUnload))
            {
                try
                {
                    var psi = new ProcessStartInfo(ollamaCli, $"stop {modelToUnload}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(2000);
                    unloadedAny = true;
                }
                catch { }
            }

            // 2. LM Studio CLI: lms unload --all
            string? lmsCli = FindLmsCli();
            if (lmsCli != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(lmsCli, "unload --all")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(6000);
                    unloadedAny = true;
                    details.Append("LM Studio: unloaded. ");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Translation] lms unload error: {ex.Message}");
                }
            }

            // 2b. Clean up any orphaned llama-server processes if LM Studio is not active
            try
            {
                var lmsProcs = Process.GetProcessesByName("LM Studio");
                if (lmsProcs.Length == 0)
                {
                    foreach (var lp in Process.GetProcessesByName("llama-server"))
                    {
                        try { lp.Kill(); unloadedAny = true; } catch { }
                    }
                }
            }
            catch { }

            lock (_lock)
            {
                _vramTimer?.Dispose();
                _vramTimer = null;
                AutoUnloadTime = null;
                IsVramLoaded = false;
            }

            SetStatus(TranslationStatusKind.VramFreed);
            VramStateChanged?.Invoke();

            string msg = unloadedAny
                ? $"{Loc.Get("Translate_VramFreed")}. {details}".Trim()
                : Loc.Get("Translate_VramFreed");

            Logger.Log($"[Translation] FreeVramAsync: {msg}");
            return (true, msg);
            }
            finally
            {
                _modelLoadLock.Release();
            }
        }

        #endregion

        #region Helpers

        private static (string srcName, string tgtName) ResolveLanguageNames(string text, string targetCode)
        {
            string srcName = "English";
            string tgtName = "Russian";

            bool hasCyrillic = Regex.IsMatch(text, @"[\u0400-\u04FF]");

            if (targetCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (hasCyrillic)
                {
                    srcName = "Russian";
                    tgtName = "English";
                }
                else
                {
                    srcName = "English";
                    tgtName = "Russian";
                }
            }
            else
            {
                srcName = hasCyrillic ? "Russian" : "English";
                foreach (var (code, enName, localName) in SupportedLanguages)
                {
                    if (code.Equals(targetCode, StringComparison.OrdinalIgnoreCase))
                    {
                        tgtName = $"{enName} ({localName})";
                        break;
                    }
                }
            }

            return (srcName, tgtName);
        }

        private static string ExtractTextFromChatResponse(string json, out string modelUsed)
        {
            modelUsed = string.Empty;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("model", out var modelElem))
                modelUsed = modelElem.GetString() ?? "";

            if (!root.TryGetProperty("choices", out var choicesElem) || choicesElem.GetArrayLength() == 0)
                return string.Empty;

            var firstChoice = choicesElem[0];
            if (!firstChoice.TryGetProperty("message", out var msgElem))
                return string.Empty;

            string content = string.Empty;
            if (msgElem.TryGetProperty("content", out var contentElem))
                content = contentElem.GetString() ?? string.Empty;

            // Handle reasoning fallback if content is blank
            if (string.IsNullOrWhiteSpace(content))
            {
                if (msgElem.TryGetProperty("reasoning", out var reasonElem) ||
                    msgElem.TryGetProperty("reasoning_content", out reasonElem))
                {
                    string r = reasonElem.GetString() ?? "";
                    var match = Regex.Match(r, @"[""«]([^""»\n]{2,})[""»]");
                    if (match.Success) content = match.Groups[1].Value.Trim();
                }
            }

            // Strip <think>...</think> blocks
            if (content.Contains("<think>"))
            {
                if (content.Contains("</think>"))
                {
                    content = Regex.Replace(content, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
                }
                else
                {
                    content = content.Replace("<think>", "").Trim();
                }
            }

            // Strip outer markdown fences if the whole response is enclosed
            if (content.StartsWith("```") && content.EndsWith("```"))
            {
                int firstNewline = content.IndexOf('\n');
                int lastFence = content.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                {
                    content = content.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }
            }

            return content.Trim();
        }

        public void Dispose()
        {
            _vramTimer?.Dispose();
            _httpClient?.Dispose();
        }

        #endregion
    }
}
