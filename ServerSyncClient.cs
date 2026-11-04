using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuietPro
{
    /// <summary>
    /// Background telemetry client for ServerSync exchange server.
    /// Consumes 0 resources when disabled.
    /// Provides real-time per-file and queue sync progress with adaptive polling.
    /// </summary>
    public class ServerSyncClient : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly HttpClient _http;
        private CancellationTokenSource? _cts;
        private Task? _pollTask;
        private readonly object _lock = new();

        public bool IsEnabled => _settings.ServerSyncEnabled;
        public bool IsOnline { get; private set; }
        public bool IsSyncing { get; private set; }
        public bool PeerConnected { get; private set; }
        public string? PeerName { get; private set; }
        public string Direction { get; private set; } = "idle"; // upload, download, idle, offline

        // Current file progress
        public string CurrentFileName { get; private set; } = "";
        public string CurrentFileRelPath { get; private set; } = "";
        public long CurrentTransferredBytes { get; private set; }
        public long CurrentTotalBytes { get; private set; }
        public float CurrentFilePercent { get; private set; }
        public float CurrentSpeedMbps { get; private set; }

        // Overall queue progress
        public int QueueFilesCount { get; private set; }
        public long QueueTotalBytes { get; private set; }
        public float OverallPercent { get; private set; }
        public int EtaSeconds { get; private set; }

        // Deletion status tracking
        public bool IsDeleting { get; private set; }
        public string LastDeletedName { get; private set; } = "";
        public bool LastDeletedIsDir { get; private set; }
        public DateTime LastDeleteTime { get; private set; } = DateTime.MinValue;

        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;

        public event Action? ProgressUpdated;
        public event Action? StatusChanged;

        public ServerSyncClient(AppSettings settings)
        {
            _settings = settings;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(4000)
            };

            if (_settings.ServerSyncEnabled)
            {
                StartPolling();
            }
        }

        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                if (_settings.ServerSyncEnabled == enabled) return;
                _settings.ServerSyncEnabled = enabled;
                _settings.Save();

                if (enabled)
                {
                    StartPolling();
                }
                else
                {
                    StopPolling();
                    ResetState();
                }
            }
            StatusChanged?.Invoke();
            ProgressUpdated?.Invoke();
        }

        public void UpdateConfig(string host, int port)
        {
            lock (_lock)
            {
                _settings.ServerSyncHost = host.Trim();
                _settings.ServerSyncPort = port;
                _settings.Save();

                if (_settings.ServerSyncEnabled)
                {
                    RestartPolling();
                }
            }
        }

        private void StartPolling()
        {
            StopPolling();
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }

        private void StopPolling()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
                catch { }
                _cts = null;
            }
            _pollTask = null;
        }

        private void RestartPolling()
        {
            StartPolling();
        }

        private void ResetState()
        {
            IsOnline = false;
            IsSyncing = false;
            PeerConnected = false;
            PeerName = null;
            Direction = "offline";
            CurrentFileName = "";
            CurrentFileRelPath = "";
            CurrentTransferredBytes = 0;
            CurrentTotalBytes = 0;
            CurrentFilePercent = 0f;
            CurrentSpeedMbps = 0f;
            QueueFilesCount = 0;
            QueueTotalBytes = 0;
            OverallPercent = 100f;
            EtaSeconds = 0;
            IsDeleting = false;
            LastDeletedName = "";
            LastDeletedIsDir = false;
            LastDeleteTime = DateTime.MinValue;
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _settings.ServerSyncEnabled)
            {
                int delayMs = 2500;
                try
                {
                    string host = string.IsNullOrWhiteSpace(_settings.ServerSyncHost) ? "127.0.0.1" : _settings.ServerSyncHost.Trim();
                    int port = _settings.ServerSyncPort > 0 ? _settings.ServerSyncPort : 19888;
                    string url = $"http://{host}:{port}/progress";

                    using var resp = await _http.GetAsync(url, token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        bool prevSyncing = IsSyncing;
                        bool prevOnline = IsOnline;
                        bool prevDeleting = IsDeleting;

                        IsOnline = true;
                        IsSyncing = root.TryGetProperty("is_syncing", out var pSync) && pSync.GetBoolean();
                        PeerConnected = root.TryGetProperty("peer_connected", out var pConn) && pConn.GetBoolean();
                        PeerName = root.TryGetProperty("peer_name", out var pPeer) && pPeer.ValueKind == JsonValueKind.String ? pPeer.GetString() : null;
                        Direction = root.TryGetProperty("direction", out var pDir) && pDir.ValueKind == JsonValueKind.String ? pDir.GetString() ?? "idle" : "idle";

                        if (root.TryGetProperty("recent_delete", out var pDel) && pDel.ValueKind == JsonValueKind.Object)
                        {
                            bool active = pDel.TryGetProperty("active", out var pAct) && pAct.GetBoolean();
                            if (active)
                            {
                                IsDeleting = true;
                                LastDeletedName = pDel.TryGetProperty("name", out var pName) ? pName.GetString() ?? "" : "";
                                LastDeletedIsDir = pDel.TryGetProperty("is_dir", out var pIsDir) && pIsDir.GetBoolean();
                                LastDeleteTime = DateTime.Now;
                            }
                            else if ((DateTime.Now - LastDeleteTime).TotalSeconds >= 3.0)
                            {
                                IsDeleting = false;
                            }
                        }
                        else if ((DateTime.Now - LastDeleteTime).TotalSeconds >= 3.0)
                        {
                            IsDeleting = false;
                        }

                        if (root.TryGetProperty("current_file", out var curFile) && curFile.ValueKind == JsonValueKind.Object)
                        {
                            CurrentFileName = curFile.TryGetProperty("name", out var pName) ? pName.GetString() ?? "" : "";
                            CurrentFileRelPath = curFile.TryGetProperty("rel_path", out var pRel) ? pRel.GetString() ?? "" : "";
                            CurrentTransferredBytes = curFile.TryGetProperty("transferred_bytes", out var pTrans) ? pTrans.GetInt64() : 0;
                            CurrentTotalBytes = curFile.TryGetProperty("total_bytes", out var pTot) ? pTot.GetInt64() : 0;
                            CurrentFilePercent = curFile.TryGetProperty("percent", out var pPct) ? (float)pPct.GetDouble() : 0f;
                            CurrentSpeedMbps = curFile.TryGetProperty("speed_mbps", out var pSpd) ? (float)pSpd.GetDouble() : 0f;
                        }
                        else
                        {
                            CurrentFileName = "";
                            CurrentFileRelPath = "";
                            CurrentTransferredBytes = 0;
                            CurrentTotalBytes = 0;
                            CurrentFilePercent = 0f;
                            CurrentSpeedMbps = 0f;
                        }

                        if (root.TryGetProperty("overall", out var ovr) && ovr.ValueKind == JsonValueKind.Object)
                        {
                            QueueFilesCount = ovr.TryGetProperty("queue_files", out var pQf) ? pQf.GetInt32() : 0;
                            QueueTotalBytes = ovr.TryGetProperty("queue_bytes", out var pQb) ? pQb.GetInt64() : 0;
                            OverallPercent = ovr.TryGetProperty("percent", out var pOpct) ? (float)pOpct.GetDouble() : (IsSyncing ? 0f : 100f);
                            if (CurrentSpeedMbps <= 0f && ovr.TryGetProperty("speed_mbps", out var pOspd))
                            {
                                CurrentSpeedMbps = (float)pOspd.GetDouble();
                            }
                            EtaSeconds = ovr.TryGetProperty("eta_seconds", out var pEta) ? pEta.GetInt32() : 0;
                        }

                        LastUpdateTime = DateTime.Now;

                        // Fast adaptive polling when syncing or deleting, slower when idle
                        delayMs = (IsSyncing || IsDeleting) ? 550 : 2500;

                        if (prevSyncing != IsSyncing || prevOnline != IsOnline || prevDeleting != IsDeleting)
                        {
                            StatusChanged?.Invoke();
                        }
                        ProgressUpdated?.Invoke();
                    }
                    else
                    {
                        MarkOffline();
                        delayMs = 5000;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    MarkOffline();
                    delayMs = 4000;
                }

                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private void MarkOffline()
        {
            bool wasOnline = IsOnline;
            IsOnline = false;
            IsSyncing = false;
            PeerConnected = false;
            PeerName = null;
            Direction = "offline";
            CurrentFileName = "";
            CurrentFileRelPath = "";
            CurrentTransferredBytes = 0;
            CurrentTotalBytes = 0;
            CurrentFilePercent = 0f;
            CurrentSpeedMbps = 0f;
            QueueFilesCount = 0;
            QueueTotalBytes = 0;
            OverallPercent = 100f;
            EtaSeconds = 0;
            if (wasOnline)
            {
                StatusChanged?.Invoke();
                ProgressUpdated?.Invoke();
            }
        }

        /// <summary>
        /// Explicit diagnostic check used by the Settings UI button.
        /// </summary>
        public async Task<(bool success, string message, string? nodeName)> TestConnectionAsync(string host, int port)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                string url = $"http://{host.Trim()}:{port}/status";
                using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    string nodeName = doc.RootElement.TryGetProperty("node_name", out var pNode) ? pNode.GetString() ?? "ServerSync" : "ServerSync";
                    bool conn = doc.RootElement.TryGetProperty("connected", out var pConn) && pConn.GetBoolean();
                    string peer = doc.RootElement.TryGetProperty("peer_name", out var pPeer) ? pPeer.GetString() ?? "" : "";
                    string info = conn && !string.IsNullOrEmpty(peer) ? $"{nodeName} (↔ {peer})" : $"{nodeName} (Ожидание узла)";
                    return (true, info, nodeName);
                }
                return (false, $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}", null);
            }
            catch (OperationCanceledException)
            {
                return (false, Loc.Get("ServerSync_Timeout"), null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        public void Dispose()
        {
            StopPolling();
            _http.Dispose();
        }
    }
}
