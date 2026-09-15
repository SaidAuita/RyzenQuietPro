using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RyzenQuietPro
{
    public class GpuDeviceInfo
    {
        public uint Index { get; }
        public IntPtr Handle { get; }
        public string Name { get; set; } = "GPU";
        public string VramType { get; set; } = "GDDR6";
        public float GpuLoadPercent { get; set; }
        public float VramLoadPercent { get; set; }
        public ulong VramTotalBytes { get; set; }
        public ulong VramUsedBytes { get; set; }
        public float GpuTemperatureC { get; set; }
        public float GpuPowerWatts { get; set; }
        public uint FanSpeedPercent { get; set; }
        public double VramTotalGb => VramTotalBytes / (1024.0 * 1024 * 1024);
        public double VramUsedGb => VramUsedBytes / (1024.0 * 1024 * 1024);

        private readonly List<float> _gpuHistory = new();
        private readonly List<float> _vramHistory = new();
        private readonly List<float> _tempHistory = new();
        private readonly object _lock = new();

        public IReadOnlyList<float> GpuHistory { get { lock (_lock) return _gpuHistory.ToArray(); } }
        public IReadOnlyList<float> VramHistory { get { lock (_lock) return _vramHistory.ToArray(); } }
        public IReadOnlyList<float> TempHistory { get { lock (_lock) return _tempHistory.ToArray(); } }

        public GpuDeviceInfo(uint index, IntPtr handle, string name)
        {
            Index = index;
            Handle = handle;
            Name = name;
            VramType = DetectVramType(name);
            lock (_lock)
            {
                for (int i = 0; i < 60; i++)
                {
                    _gpuHistory.Add(0f);
                    _vramHistory.Add(0f);
                    _tempHistory.Add(0f);
                }
            }
        }

        public static string DetectVramType(string gpuName)
        {
            if (string.IsNullOrWhiteSpace(gpuName)) return "GDDR6";

            string upper = gpuName.ToUpperInvariant();

            if (upper.Contains("RADEON(TM) GRAPHICS") || upper.Contains("RADEON GRAPHICS") ||
                (upper.Contains("VEGA") && (upper.Contains("3") || upper.Contains("6") || upper.Contains("7") || upper.Contains("8") || upper.Contains("11"))) ||
                upper.Contains("680M") || upper.Contains("780M") || upper.Contains("890M") ||
                upper.Contains("IRIS") || upper.Contains("UHD GRAPHICS") || upper.Contains("HD GRAPHICS") ||
                upper.Contains("INTEL GRAPHICS"))
            {
                return "Shared";
            }

            if (upper.Contains("VEGA 56") || upper.Contains("VEGA 64") || upper.Contains("RADEON VII") || upper.Contains("TITAN V"))
            {
                return "HBM2";
            }

            if (!upper.Contains("LAPTOP") && !upper.Contains("MOBILE"))
            {
                if (upper.Contains("4090") || upper.Contains("4080") || upper.Contains("4070 TI") || upper.Contains("4070 SUPER") || upper.Contains("RTX 4070") ||
                    upper.Contains("3090") || upper.Contains("3080") || upper.Contains("3070 TI"))
                {
                    return "GDDR6X";
                }
            }

            if (upper.Contains("1080 TI") || upper.Contains("GTX 1080") || upper.Contains("TITAN X"))
            {
                return "GDDR5X";
            }

            if (upper.Contains("GTX 1070") || upper.Contains("GTX 1060") || upper.Contains("GTX 1050") ||
                upper.Contains("GTX 980") || upper.Contains("GTX 970") || upper.Contains("GTX 960") || upper.Contains("GTX 950") ||
                upper.Contains("GTX 780") || upper.Contains("GTX 770") || upper.Contains("GTX 760") ||
                upper.Contains("RX 590") || upper.Contains("RX 580") || upper.Contains("RX 570") || upper.Contains("RX 560") || upper.Contains("RX 550") ||
                upper.Contains("RX 480") || upper.Contains("RX 470") || upper.Contains("RX 460"))
            {
                return "GDDR5";
            }

            return "GDDR6";
        }

        public void AddHistory(float gpuLoad, float vramLoad, float temp = 0f)
        {
            lock (_lock)
            {
                _gpuHistory.Add(gpuLoad);
                while (_gpuHistory.Count > 60) _gpuHistory.RemoveAt(0);

                _vramHistory.Add(vramLoad);
                while (_vramHistory.Count > 60) _vramHistory.RemoveAt(0);

                if (temp > 0f && _tempHistory.Count > 0 && _tempHistory[0] == 0f)
                {
                    bool allZero = true;
                    for (int i = 0; i < _tempHistory.Count; i++)
                    {
                        if (_tempHistory[i] > 0f) { allZero = false; break; }
                    }
                    if (allZero)
                    {
                        for (int i = 0; i < _tempHistory.Count; i++) _tempHistory[i] = temp;
                    }
                }

                _tempHistory.Add(temp);
                while (_tempHistory.Count > 60) _tempHistory.RemoveAt(0);
            }
        }
    }

    public class GpuMonitor : IDisposable
    {
        private enum GpuBackend
        {
            None,
            NvidiaNvml,
            AmdAdl,
            WindowsPerf
        }

        private GpuBackend _backend = GpuBackend.None;
        private bool _isInitialized = false;

        private readonly List<GpuDeviceInfo> _devices = new();
        private readonly object _lock = new();

        public IReadOnlyList<GpuDeviceInfo> Devices { get { lock (_lock) return _devices.ToArray(); } }
        public int GpuCount { get { lock (_lock) return _devices.Count; } }

        public string GpuName => _devices.Count > 0 ? _devices[0].Name : "GPU";
        public float GpuLoadPercent => _devices.Count > 0 ? _devices[0].GpuLoadPercent : 0f;
        public float VramLoadPercent => _devices.Count > 0 ? _devices[0].VramLoadPercent : 0f;
        public ulong VramTotalBytes => _devices.Count > 0 ? _devices[0].VramTotalBytes : 0;
        public ulong VramUsedBytes => _devices.Count > 0 ? _devices[0].VramUsedBytes : 0;
        public float GpuTemperatureC => _devices.Count > 0 ? _devices[0].GpuTemperatureC : 0f;
        public float GpuPowerWatts => _devices.Count > 0 ? _devices[0].GpuPowerWatts : 0f;
        public uint FanSpeedPercent => _devices.Count > 0 ? _devices[0].FanSpeedPercent : 0;
        public double VramTotalGb => _devices.Count > 0 ? _devices[0].VramTotalGb : 0.0;
        public double VramUsedGb => _devices.Count > 0 ? _devices[0].VramUsedGb : 0.0;
        public string VramType => _devices.Count > 0 ? _devices[0].VramType : "GDDR6";

        public IReadOnlyList<float> GpuHistory => _devices.Count > 0 ? _devices[0].GpuHistory : Array.Empty<float>();
        public IReadOnlyList<float> VramHistory => _devices.Count > 0 ? _devices[0].VramHistory : Array.Empty<float>();
        public IReadOnlyList<float> TempHistory => _devices.Count > 0 ? _devices[0].TempHistory : Array.Empty<float>();

        public GpuMonitor()
        {
            InitializeBackend();
            Sample();
        }

        private void InitializeBackend()
        {
            // 1. Try NVIDIA NVML first
            if (TryInitNvidia())
            {
                _backend = GpuBackend.NvidiaNvml;
                Logger.Log($"GpuMonitor initialized with NVIDIA NVML: {_devices.Count} GPU(s) found. Primary: {GpuName}");
                return;
            }

            // 2. Try AMD ADL
            if (TryInitAmd())
            {
                _backend = GpuBackend.AmdAdl;
                Logger.Log($"GpuMonitor initialized with AMD ADL: {_devices.Count} GPU(s) found. Primary: {GpuName}");
                return;
            }

            // 3. Fallback to Windows / WMI fallback
            _backend = GpuBackend.WindowsPerf;
            TryInitWindowsFallback();
            Logger.Log($"GpuMonitor initialized with Windows Fallback: {GpuName}");
        }

        #region NVIDIA NVML

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        private static extern int NvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
        private static extern int NvmlDeviceGetCount_v2(out uint deviceCount);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount")]
        private static extern int NvmlDeviceGetCount(out uint deviceCount);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
        private static extern int NvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        private static extern int NvmlDeviceGetUtilizationRates(IntPtr device, ref NvmlUtilization utilization);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        private static extern int NvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int NvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
        private static extern int NvmlDeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed")]
        private static extern int NvmlDeviceGetFanSpeed(IntPtr device, out uint speed);

        private bool TryInitNvidia()
        {
            try
            {
                string sysDir = Environment.SystemDirectory;
                string nvmlPath = Path.Combine(sysDir, "nvml.dll");
                if (!File.Exists(nvmlPath)) return false;

                int ret = NvmlInit();
                if (ret != 0) return false;

                uint count = 0;
                int countRet = -1;
                try { countRet = NvmlDeviceGetCount_v2(out count); }
                catch {
                    try { countRet = NvmlDeviceGetCount(out count); } catch { }
                }

                if (countRet != 0 || count == 0) count = 1;

                lock (_lock)
                {
                    _devices.Clear();
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr dev = IntPtr.Zero;
                        if (NvmlDeviceGetHandleByIndex(i, out dev) == 0 && dev != IntPtr.Zero)
                        {
                            byte[] nameBuf = new byte[64];
                            NvmlDeviceGetName(dev, nameBuf, 64);
                            string name = System.Text.Encoding.ASCII.GetString(nameBuf).TrimEnd('\0');
                            if (string.IsNullOrWhiteSpace(name)) name = (i == 0) ? "NVIDIA GeForce" : $"NVIDIA GPU #{i}";

                            _devices.Add(new GpuDeviceInfo(i, dev, name));
                        }
                    }
                }

                if (_devices.Count == 0)
                {
                    NvmlShutdown();
                    return false;
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"NVML initialization skipped: {ex.Message}");
                return false;
            }
        }

        private void SampleNvidia()
        {
            try
            {
                lock (_lock)
                {
                    foreach (var dev in _devices)
                    {
                        var util = new NvmlUtilization();
                        if (NvmlDeviceGetUtilizationRates(dev.Handle, ref util) == 0)
                        {
                            dev.GpuLoadPercent = Math.Clamp((float)util.gpu, 0f, 100f);
                        }

                        var mem = new NvmlMemory();
                        if (NvmlDeviceGetMemoryInfo(dev.Handle, ref mem) == 0 && mem.total > 0)
                        {
                            dev.VramTotalBytes = mem.total;
                            dev.VramUsedBytes = mem.used;
                            dev.VramLoadPercent = Math.Clamp((float)(mem.used * 100.0 / mem.total), 0f, 100f);
                        }

                        uint temp = 0;
                        if (NvmlDeviceGetTemperature(dev.Handle, 0, out temp) == 0)
                        {
                            dev.GpuTemperatureC = temp;
                        }

                        uint powerMw = 0;
                        if (NvmlDeviceGetPowerUsage(dev.Handle, out powerMw) == 0)
                        {
                            dev.GpuPowerWatts = powerMw / 1000f;
                        }

                        uint fanSpeed = 0;
                        if (NvmlDeviceGetFanSpeed(dev.Handle, out fanSpeed) == 0)
                        {
                            dev.FanSpeedPercent = fanSpeed;
                        }

                        dev.AddHistory(dev.GpuLoadPercent, dev.VramLoadPercent, dev.GpuTemperatureC);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SampleNvidia error: {ex.Message}");
            }
        }

        #endregion

        #region AMD ADL

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLTemperature
        {
            public int iSize;
            public int iTemperature;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLPMActivity
        {
            public int iSize;
            public int iEngineClock;
            public int iMemoryClock;
            public int iVddc;
            public int iActivityPercent;
            public int iCurrentPerformanceLevel;
            public int iCurrentBusSpeed;
            public int iCurrentBusLanes;
            public int iMaximumBusLanes;
            public int iReserved;
        }

        [DllImport("atiadlxx.dll")]
        private static extern int ADL_Main_Control_Create(IntPtr callback, int enumConnectedAdapters);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL_Main_Control_Destroy();

        [DllImport("atiadlxx.dll")]
        private static extern int ADL_Overdrive5_CurrentActivity_Get(int iAdapterIndex, ref ADLPMActivity activity);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL_Overdrive5_Temperature_Get(int iAdapterIndex, int iThermalControllerIndex, ref ADLTemperature temperature);

        private bool _amdInitialized = false;

        private bool TryInitAmd()
        {
            try
            {
                string sysDir = Environment.SystemDirectory;
                string adlPath = Path.Combine(sysDir, "atiadlxx.dll");
                if (!File.Exists(adlPath)) return false;

                int ret = ADL_Main_Control_Create(IntPtr.Zero, 1);
                if (ret == 0)
                {
                    _amdInitialized = true;
                    lock (_lock)
                    {
                        _devices.Clear();
                        _devices.Add(new GpuDeviceInfo(0, IntPtr.Zero, "AMD Radeon GPU"));
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"AMD ADL init skipped: {ex.Message}");
            }
            return false;
        }

        private void SampleAmd()
        {
            try
            {
                var act = new ADLPMActivity { iSize = Marshal.SizeOf<ADLPMActivity>() };
                float load = 0f;
                if (ADL_Overdrive5_CurrentActivity_Get(0, ref act) == 0)
                {
                    load = Math.Clamp((float)act.iActivityPercent, 0f, 100f);
                }

                var temp = new ADLTemperature { iSize = Marshal.SizeOf<ADLTemperature>() };
                float temperature = 0f;
                if (ADL_Overdrive5_Temperature_Get(0, 0, ref temp) == 0)
                {
                    temperature = (float)(temp.iTemperature / 1000.0);
                }

                lock (_lock)
                {
                    if (_devices.Count > 0)
                    {
                        _devices[0].GpuLoadPercent = load;
                        _devices[0].GpuTemperatureC = temperature;
                        _devices[0].AddHistory(load, _devices[0].VramLoadPercent, temperature);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SampleAmd error: {ex.Message}");
            }
        }

        #endregion

        #region Windows Performance Counters Fallback

        private void TryInitWindowsFallback()
        {
            string name = "Discrete / Integrated GPU";
            ulong vramTotal = 8UL * 1024 * 1024 * 1024;

            try
            {
                using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (baseKey != null)
                {
                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        if (subName.StartsWith("00"))
                        {
                            using var subKey = baseKey.OpenSubKey(subName);
                            if (subKey != null)
                            {
                                string? desc = subKey.GetValue("DriverDesc") as string;
                                if (!string.IsNullOrEmpty(desc) && !desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = desc;
                                    object? memVal = subKey.GetValue("HardwareInformation.qwMemorySize");
                                    if (memVal is long lVal && lVal > 0)
                                    {
                                        vramTotal = (ulong)lVal;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Windows fallback registry query error: {ex.Message}");
            }

            lock (_lock)
            {
                _devices.Clear();
                var dev = new GpuDeviceInfo(0, IntPtr.Zero, name)
                {
                    VramTotalBytes = vramTotal
                };
                _devices.Add(dev);
            }
        }

        private void SampleWindowsFallback()
        {
            lock (_lock)
            {
                if (_devices.Count > 0)
                {
                    _devices[0].AddHistory(_devices[0].GpuLoadPercent, _devices[0].VramLoadPercent, _devices[0].GpuTemperatureC);
                }
            }
        }

        #endregion

        public void Sample()
        {
            try
            {
                switch (_backend)
                {
                    case GpuBackend.NvidiaNvml:
                        SampleNvidia();
                        break;
                    case GpuBackend.AmdAdl:
                        SampleAmd();
                        break;
                    case GpuBackend.WindowsPerf:
                    default:
                        SampleWindowsFallback();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GpuMonitor sample error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_backend == GpuBackend.NvidiaNvml && _isInitialized)
                {
                    NvmlShutdown();
                    _isInitialized = false;
                }
                else if (_backend == GpuBackend.AmdAdl && _amdInitialized)
                {
                    ADL_Main_Control_Destroy();
                    _amdInitialized = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GpuMonitor dispose error: {ex.Message}");
            }
        }
    }
}
