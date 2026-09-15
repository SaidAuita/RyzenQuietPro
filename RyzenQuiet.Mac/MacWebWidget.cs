using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RyzenQuiet.Mac
{
    public class MacWebWidget
    {
        private readonly MacCpuMonitor _cpu;
        private readonly MacRamMonitor _ram;
        private readonly MacGpuMonitor _gpu;
        private readonly MacDiskMonitor _disk;
        private readonly MacFanMonitor _fans;
        private readonly MacPowerManager _power;
        private HttpListener? _listener;

        public int Port { get; } = 5050;

        public MacWebWidget(
            MacCpuMonitor cpu,
            MacRamMonitor ram,
            MacGpuMonitor gpu,
            MacDiskMonitor disk,
            MacFanMonitor fans,
            MacPowerManager power)
        {
            _cpu = cpu;
            _ram = ram;
            _gpu = gpu;
            _disk = disk;
            _fans = fans;
            _power = power;
        }

        public void Start(CancellationToken ct)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://*:{Port}/");
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();

                _ = Task.Run(() => ListenLoop(ct), ct);
            }
            catch
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{Port}/");
                    _listener.Start();
                    _ = Task.Run(() => ListenLoop(ct), ct);
                }
                catch { }
            }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch { }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (path == "/api/status")
                {
                    HandleApiStatus(ctx);
                }
                else if (path == "/api/mode")
                {
                    string mode = ctx.Request.QueryString["m"] ?? "";
                    if (mode.Equals("boost", StringComparison.OrdinalIgnoreCase))
                    {
                        _power.SetMode(MacPowerManager.Mode.Boost);
                    }
                    else
                    {
                        _power.SetMode(MacPowerManager.Mode.Quiet);
                    }
                    HandleApiStatus(ctx);
                }
                else if (path == "/api/demo")
                {
                    _fans.DemoMode = !_fans.DemoMode;
                    HandleApiStatus(ctx);
                }
                else
                {
                    HandleHtml(ctx);
                }
            }
            catch { }
        }

        private void HandleApiStatus(HttpListenerContext ctx)
        {
            var data = new
            {
                isQuiet = _power.IsQuietMode,
                modeText = _power.StatusDescription,
                cpu = new
                {
                    name = _cpu.CpuName,
                    percent = _cpu.TotalPercent,
                    ghz = _cpu.CurrentGhz,
                    threads = _cpu.LogicalCores,
                    cores = _cpu.PhysicalCores,
                    coreLoads = _cpu.CoreLoads,
                    history = _cpu.History
                },
                ram = new
                {
                    specs = _ram.MemorySpecs,
                    used = _ram.UsedGb,
                    total = _ram.TotalGb,
                    percent = _ram.UsagePercent,
                    history = _ram.History
                },
                gpu = new
                {
                    name = _gpu.GpuName,
                    vramType = _gpu.VramType,
                    vramUsed = _gpu.VramUsedGb,
                    vramTotal = _gpu.VramTotalGb,
                    load = _gpu.GpuLoadPercent,
                    history = _gpu.History
                },
                disk = new
                {
                    name = _disk.ActiveDiskName,
                    read = _disk.ReadSpeedMBps,
                    write = _disk.WriteSpeedMBps,
                    percent = _disk.ActivityPercent,
                    history = _disk.History
                },
                fans = new
                {
                    demo = _fans.DemoMode,
                    items = _fans.Fans
                }
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = jsonBytes.Length;
            ctx.Response.OutputStream.Write(jsonBytes, 0, jsonBytes.Length);
            ctx.Response.Close();
        }

        private void HandleHtml(HttpListenerContext ctx)
        {
            string html = @"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""UTF-8"">
<title>⚡ RyzenQuiet PRO (macOS Edition)</title>
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; user-select: none; }
  body { background: #121318; color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; padding: 12px; }
  .card { background: #1a1c24; border: 1px solid #2b3040; border-radius: 8px; padding: 10px 14px; margin-bottom: 10px; }
  .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #2b3040; padding-bottom: 8px; margin-bottom: 10px; }
  .title { font-size: 15px; font-weight: bold; color: #38bdf8; }
  .mode-badge { font-size: 11px; padding: 3px 8px; border-radius: 4px; font-weight: bold; }
  .mode-quiet { background: #166534; color: #bbf7d0; border: 1px solid #22c55e; }
  .mode-boost { background: #c2410c; color: #ffedd5; border: 1px solid #f97316; }
  .metric-row { display: flex; justify-content: space-between; margin-bottom: 4px; font-size: 13px; }
  .metric-sub { font-size: 11px; color: #94a3b8; }
  canvas { width: 100%; height: 38px; background: #101116; border-radius: 4px; margin-top: 4px; display: block; }
  .btn-row { display: flex; gap: 8px; margin-top: 10px; }
  .btn { flex: 1; padding: 8px; border-radius: 6px; border: none; font-size: 12px; font-weight: bold; cursor: pointer; text-align: center; }
  .btn-quiet { background: #1e3a8a; color: #bfdbfe; }
  .btn-boost { background: #831843; color: #fbcfe8; }
  .cores { display: flex; gap: 4px; margin-top: 6px; }
  .core-box { flex: 1; height: 18px; border-radius: 3px; background: #22c55e; text-align: center; font-size: 9px; line-height: 18px; font-weight: bold; color: #000; }
  .fans-container { display: flex; gap: 8px; overflow-x: auto; padding-bottom: 4px; }
  .fan-card { min-width: 80px; background: #181920; border: 1px solid #2e3446; border-radius: 6px; padding: 6px; text-align: center; }
  .fan-icon { font-size: 20px; animation: spin 1s linear infinite; display: inline-block; }
  @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
</style>
</head>
<body>
<div class=""card"">
  <div class=""header"">
    <div class=""title"">⚡ RyzenQuiet PRO <span style=""font-size:11px;color:#94a3b8;"">macOS</span></div>
    <div id=""modeBadge"" class=""mode-badge mode-quiet"">🌿 SILENT</div>
  </div>

  <div class=""metric-row"">
    <span style=""color:#22c55e;font-weight:bold;"">CPU</span>
    <span id=""cpuSub"" class=""metric-sub"">Intel Core | 2.60 GHz</span>
  </div>
  <canvas id=""cvCpu""></canvas>
  <div id=""coreHeatmap"" class=""cores""></div>

  <div style=""height:10px;""></div>
  <div class=""metric-row"">
    <span style=""color:#06b6d4;font-weight:bold;"">RAM</span>
    <span id=""ramSub"" class=""metric-sub"">DDR3/1600 | 4.2 / 8.0 GB</span>
  </div>
  <canvas id=""cvRam""></canvas>

  <div style=""height:10px;""></div>
  <div class=""metric-row"">
    <span style=""color:#a855f7;font-weight:bold;"">GPU</span>
    <span id=""gpuSub"" class=""metric-sub"">Intel HD 4000</span>
  </div>
  <canvas id=""cvGpu""></canvas>

  <div style=""height:10px;""></div>
  <div class=""metric-row"">
    <span style=""color:#eab308;font-weight:bold;"">DISK</span>
    <span id=""diskSub"" class=""metric-sub"">/ (Macintosh HD)</span>
  </div>
  <canvas id=""cvDisk""></canvas>

  <div style=""height:10px;""></div>
  <div class=""metric-row"">
    <span style=""color:#38bdf8;font-weight:bold;"">FANS</span>
    <span id=""fanSub"" class=""metric-sub"">AppleSMC</span>
  </div>
  <div id=""fanCards"" class=""fans-container""></div>

  <div class=""btn-row"">
    <button class=""btn btn-quiet"" onclick=""setMode('quiet')"">🌿 Silent (99%)</button>
    <button class=""btn btn-boost"" onclick=""setMode('boost')"">⚡ Boost (100%)</button>
  </div>
</div>

<script>
function drawGraph(canvasId, history, color) {
  const c = document.getElementById(canvasId);
  if (!c) return;
  const ctx = c.getContext('2d');
  c.width = c.clientWidth * 2;
  c.height = c.clientHeight * 2;
  ctx.clearRect(0,0,c.width,c.height);
  if (!history || history.length < 2) return;
  ctx.beginPath();
  const step = c.width / (history.length - 1);
  for(let i=0; i<history.length; i++) {
    const y = c.height - (history[i] / 100) * c.height;
    if (i===0) ctx.moveTo(0, y); else ctx.lineTo(i*step, y);
  }
  ctx.strokeStyle = color;
  ctx.lineWidth = 3;
  ctx.stroke();
}

async function updateData() {
  try {
    const res = await fetch('/api/status');
    const d = await res.json();
    const mb = document.getElementById('modeBadge');
    mb.innerText = d.isQuiet ? '🌿 SILENT 99%' : '⚡ BOOST 100%';
    mb.className = 'mode-badge ' + (d.isQuiet ? 'mode-quiet' : 'mode-boost');

    document.getElementById('cpuSub').innerText = d.cpu.name + ' | ' + d.cpu.ghz.toFixed(2) + ' GHz | ' + d.cpu.percent.toFixed(1) + '%';
    document.getElementById('ramSub').innerText = d.ram.specs + ' | ' + d.ram.used.toFixed(1) + ' / ' + d.ram.total.toFixed(1) + ' GB';
    document.getElementById('gpuSub').innerText = d.gpu.name + ' | ' + d.gpu.vramType + ' ' + d.gpu.vramUsed.toFixed(1) + ' GB';
    document.getElementById('diskSub').innerText = d.disk.name + ' | R:' + d.disk.read.toFixed(1) + ' W:' + d.disk.write.toFixed(1) + ' MB/s';

    drawGraph('cvCpu', d.cpu.history, '#22c55e');
    drawGraph('cvRam', d.ram.history, '#06b6d4');
    drawGraph('cvGpu', d.gpu.history, '#a855f7');
    drawGraph('cvDisk', d.disk.history, '#eab308');

    // Heatmap
    const hm = document.getElementById('coreHeatmap');
    hm.innerHTML = '';
    d.cpu.coreLoads.forEach((c, idx) => {
      const col = c > 75 ? '#ef4444' : c > 40 ? '#f59e0b' : '#22c55e';
      hm.innerHTML += `<div class=""core-box"" style=""background:${col};"">T${idx+1}</div>`;
    });

    // Fans
    const fc = document.getElementById('fanCards');
    fc.innerHTML = '';
    d.fans.items.forEach(f => {
      const spd = f.CurrentRpm > 0 ? (1000 / Math.max(200, f.CurrentRpm)).toFixed(2) : 0;
      const anim = spd > 0 ? `style=""animation: spin ${spd}s linear infinite; color:${f.ColorHex};""` : `style=""color:#64748b;""`;
      fc.innerHTML += `<div class=""fan-card""><div class=""fan-icon"" ${anim}>🌀</div><div style=""font-size:10px;font-weight:bold;margin-top:2px;"">${f.Name}</div><div style=""font-size:9px;color:${f.ColorHex};"">${f.CurrentRpm} RPM</div></div>`;
    });

  } catch(e) {}
}

function setMode(m) {
  fetch('/api/mode?m=' + m).then(updateData);
}

setInterval(updateData, 1000);
updateData();
</script>
</body>
</html>";

            byte[] buf = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
    }
}
