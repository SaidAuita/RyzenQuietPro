using System;
using System.Collections.Generic;
using System.Globalization;

namespace RyzenQuietPro
{
    public static class Loc
    {
        public static readonly (string Code, string Name, string Flag)[] SupportedLanguages = new[]
        {
            ("ru", "Русский", "🇷🇺"),
            ("en", "English", "🇬🇧"),
            ("de", "Deutsch", "🇩🇪"),
            ("es", "Español", "🇪🇸"),
            ("fr", "Français", "🇫🇷"),
            ("ja", "日本語", "🇯🇵"),
            ("pt", "Português", "🇵🇹"),
            ("zh", "中文", "🇨🇳")
        };

        private static string _currentLang = "en";

        public static string CurrentLanguage => _currentLang;

        public static event Action? LanguageChanged;

        private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
        {
            ["ru"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Тихий",
                ["SilentFull"] = "🌿 Тихий (99%)",
                ["Boost"] = "Турбо",
                ["BoostActive"] = "⚡ Турбо (100%)",
                ["Threads"] = "потоков",
                ["ThreadsActive"] = "потоков активно",
                ["AllDisksIdle"] = "Все накопители в простое",
                ["Idle"] = "Простой",
                ["ShowAllGpus"] = "Все GPU",
                
                // Headers
                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DISK",
                ["Fans"] = "КУЛЕРЫ",
                
                // Tooltips
                ["TipInfo"] = "Характеристики системы и совместимость",
                ["TipPinOn"] = "Поверх окон (Вкл)",
                ["TipPinOff"] = "Поверх окон (Выкл)",
                ["TipDetach"] = "Открепить виджет на рабочий стол",
                ["TipDock"] = "Прикрепить к панели задач",
                ["TipClose"] = "Свернуть в трей",
                
                // Menu: Graphs
                ["MenuGraphs"] = "Графики и модули:",
                ["CpuGraph"] = "📊 График CPU (Загрузка)",
                ["CpuCores"] = "🔳 Матрица ядер CPU (16/32)",
                ["TopProcesses"] = "⚡ Топ-5 процессов по CPU",
                ["RamGraph"] = "🧠 График RAM (Память)",
                ["GpuGraph"] = "🎮 График GPU (Видеокарта)",
                ["GpuTemp"] = "🌡️ Линия температуры GPU",
                ["GpuFan"] = "💨 Обороты кулеров GPU",
                ["VramGraph"] = "📼 График VRAM (Видеопамять)",
                ["DiskGraph"] = "💽 График Дисков (SSD/HDD)",
                ["FanGraph"] = "❄️ График кулеров (RPM) [Дополнение]",
                ["ShowAllGraphs"] = "Показать все графики",
                
                // Fan Plugin
                ["FanPluginStatus_Connected"] = "● Активно",
                ["FanPluginStatus_Connecting"] = "● Подключение...",
                ["FanPluginStatus_NotInstalled"] = "⚪ Дополнение не найдено",
                ["FanPluginStatus_Disabled"] = "Выключено",
                ["FanPluginStatus_NeedAdmin"] = "🟡 Требуются права Администратора",
                ["FanPluginStatus_NoFans"] = "Датчики кулеров не найдены (EC заблокирован)",
                ["FanPluginFolder"] = "Папка дополнения",
                ["FanPlugin_Start"] = "▶ Старт",
                ["FanPlugin_Stop"] = "⏹ Стоп",
                ["FanPlugin_Stopped"] = "⚪ Остановлено",
                ["FanPlugin_TipToggle"] = "Запуск / остановка фонового дополнения кулеров",
                ["FanVisualMode_Graph"] = "📈 График",
                ["FanVisualMode_Icons"] = "🌀 В 1 ряд",
                ["FanVisualMode_Grid"] = "▦ Сетка",
                ["FanDemoMode"] = "👁️ Демо",
                ["FanSelectFans"] = "⚙ Выбор",
                ["FanSelectFansFull"] = "⚙ Выбор кулеров",
                ["FanSelectionTitle"] = "Отображаемые кулеры",
                ["FanSelectionHint"] = "Отметьте кулеры для отображения в виджете. Кулеры с 0 RPM можно оставить включенными — они заработают при нагрузке.",
                ["FanSelectAll"] = "Выбрать все",
                ["FanDeselectAll"] = "Снять все",
                ["FanSelectionNoLiveFans"] = "Вентиляторы не обнаружены. Запустите службу кулеров.",
                ["FanGpuSpinTest"] = "⚡ Тест кулеров GPU (10с)",
                ["FanGpuSpinTesting"] = "⏳ Тестирование (10с)...",
                
                // Menu: Settings
                ["MenuSettings"] = "Настройки системы:",
                ["AlwaysOnTop"] = "📌 Поверх всех окон",
                ["AutoQuiet"] = "🌿 Тихий режим при простое (5 мин)",
                ["Startup"] = "🚀 Запуск вместе с Windows",
                ["TrayIconMenu"] = "🏷️ Иконка в трее",
                ["TrayMetricCpu"] = "CPU % (Процессор)",
                ["TrayMetricGpu"] = "GPU % (Видеокарта)",
                ["TrayMetricRam"] = "RAM % (Память)",
                ["TrayMetricNone"] = "Стандартная иконка",
                ["TrayMetricNoneShort"] = "Иконка",
                ["BtnSpecsShort"] = "ℹ Характеристики",
                ["Opacity"] = "👁️ Прозрачность окна",
                ["Language"] = "🌐 Язык (Language)",
                ["HardwareInfo"] = "ℹ Характеристики системы",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Выход",

                // Info Dialog
                ["InfoTitle"] = "Характеристики системы и поддержка",
                ["InfoDetected"] = "💻 ОБНАРУЖЕНО В СИСТЕМЕ",
                ["InfoSupported"] = "🚀 ПОДДЕРЖИВАЕМЫЕ АРХИТЕКТУРЫ",
                ["InfoClose"] = "Закрыть",
                ["SysMemory"] = "Системная память",
                ["DedMemory"] = "Выделенная видеопамять",
                ["PhysicalDrives"] = "Физических накопителей"
            },
            ["en"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Silent",
                ["SilentFull"] = "🌿 Silent (99%)",
                ["Boost"] = "Boost",
                ["BoostActive"] = "⚡ Boost ON (100%)",
                ["Threads"] = "Threads",
                ["ThreadsActive"] = "Threads Active",
                ["AllDisksIdle"] = "All Disks Idle",
                ["Idle"] = "Idle",
                ["ShowAllGpus"] = "Show all",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DISK",
                ["Fans"] = "FANS",

                ["TipInfo"] = "System Specs & Hardware Support",
                ["TipPinOn"] = "Always On Top (Pinned)",
                ["TipPinOff"] = "Always On Top (Unpinned)",
                ["TipDetach"] = "Detach Floating Widget",
                ["TipDock"] = "Dock to Tray",
                ["TipClose"] = "Hide to Tray",

                ["MenuGraphs"] = "Graphs & Modules:",
                ["CpuGraph"] = "📊 CPU Load Graph",
                ["CpuCores"] = "🔳 CPU Cores Heatmap (16/32)",
                ["TopProcesses"] = "⚡ Top 5 CPU Processes",
                ["RamGraph"] = "🧠 RAM Usage Graph",
                ["GpuGraph"] = "🎮 GPU Load Graph",
                ["GpuTemp"] = "🌡️ GPU Temperature Curve",
                ["GpuFan"] = "💨 GPU Fan Speed",
                ["VramGraph"] = "📼 VRAM Usage Graph",
                ["DiskGraph"] = "💽 Disk Activity Graph (SSD/HDD)",
                ["FanGraph"] = "❄️ Fan Speed Graph (RPM) [Addon]",
                ["ShowAllGraphs"] = "Show All Graphs",

                // Fan Plugin
                ["FanPluginStatus_Connected"] = "● Active",
                ["FanPluginStatus_Connecting"] = "● Connecting...",
                ["FanPluginStatus_NotInstalled"] = "⚪ Addon not found",
                ["FanPluginStatus_Disabled"] = "Disabled",
                ["FanPluginStatus_NeedAdmin"] = "🟡 Admin rights required",
                ["FanPluginStatus_NoFans"] = "No fan sensors found (EC locked by vendor)",
                ["FanPluginFolder"] = "Addon folder",
                ["FanPlugin_Start"] = "▶ Start",
                ["FanPlugin_Stop"] = "⏹ Stop",
                ["FanPlugin_Stopped"] = "⚪ Stopped",
                ["FanPlugin_TipToggle"] = "Start / Stop background fan service addon",
                ["FanVisualMode_Graph"] = "📈 Graph",
                ["FanVisualMode_Icons"] = "🌀 1 Row",
                ["FanVisualMode_Grid"] = "▦ Grid",
                ["FanDemoMode"] = "👁️ Demo",
                ["FanSelectFans"] = "⚙ Fans",
                ["FanSelectFansFull"] = "⚙ Select Fans",
                ["FanSelectionTitle"] = "Displayed Coolers",
                ["FanSelectionHint"] = "Select which fans to display in the widget. Idle 0 RPM fans can remain enabled — they will spin up under load.",
                ["FanSelectAll"] = "Select All",
                ["FanDeselectAll"] = "Clear All",
                ["FanSelectionNoLiveFans"] = "No fan sensors detected. Start fan service to scan.",
                ["FanGpuSpinTest"] = "⚡ Test GPU Fans (10s)",
                ["FanGpuSpinTesting"] = "⏳ Testing (10s)...",
 
                ["MenuSettings"] = "System Settings:",
                ["AlwaysOnTop"] = "📌 Always On Top",
                ["AutoQuiet"] = "🌿 Auto-Quiet on Idle (5 min)",
                ["Startup"] = "🚀 Start with Windows",
                ["TrayIconMenu"] = "🏷️ Dynamic Tray Icon",
                ["TrayMetricCpu"] = "CPU % (Processor)",
                ["TrayMetricGpu"] = "GPU % (Graphics)",
                ["TrayMetricRam"] = "RAM % (Memory)",
                ["TrayMetricNone"] = "Standard Status Icon",
                ["TrayMetricNoneShort"] = "Icon",
                ["BtnSpecsShort"] = "ℹ System Specs",
                ["Opacity"] = "👁️ Window Opacity",
                ["Language"] = "🌐 Language",
                ["HardwareInfo"] = "ℹ System Specs & Hardware",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Exit",

                ["InfoTitle"] = "Hardware Support & System Specs",
                ["InfoDetected"] = "💻 DETECTED ON YOUR SYSTEM",
                ["InfoSupported"] = "🚀 SUPPORTED HARDWARE ARCHITECTURES",
                ["InfoClose"] = "Close",
                ["SysMemory"] = "System Memory",
                ["DedMemory"] = "Dedicated Video Memory",
                ["PhysicalDrives"] = "Physical Drives"
            },
            ["de"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Leise",
                ["SilentFull"] = "🌿 Leise (99%)",
                ["Boost"] = "Boost",
                ["BoostActive"] = "⚡ Boost AN (100%)",
                ["Threads"] = "Threads",
                ["ThreadsActive"] = "Threads Aktiv",
                ["AllDisksIdle"] = "Alle Laufwerke im Leerlauf",
                ["Idle"] = "Leerlauf",
                ["ShowAllGpus"] = "Alle GPUs",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DATENTRÄGER",

                ["TipInfo"] = "Systemdaten & Hardware-Unterstützung",
                ["TipPinOn"] = "Immer im Vordergrund (Aktiv)",
                ["TipPinOff"] = "Immer im Vordergrund (Inaktiv)",
                ["TipDetach"] = "Als Desktop-Widget lösen",
                ["TipDock"] = "An Taskleiste andocken",
                ["TipClose"] = "In Infobereich minimieren",

                ["MenuGraphs"] = "Diagramme & Module:",
                ["CpuGraph"] = "📊 CPU-Auslastungsdiagramm",
                ["CpuCores"] = "🔳 CPU-Kerne Heatmap",
                ["TopProcesses"] = "⚡ Top 5 CPU-Prozesse",
                ["RamGraph"] = "🧠 RAM-Auslastung",
                ["GpuGraph"] = "🎮 GPU-Auslastung",
                ["GpuTemp"] = "🌡️ GPU-Temperaturkurve",
                ["GpuFan"] = "💨 GPU-Lüftergeschwindigkeit",
                ["VramGraph"] = "📼 VRAM-Diagramm",
                ["DiskGraph"] = "💽 Festplatten-Aktivität (SSD/HDD)",
                ["ShowAllGraphs"] = "Alle Diagramme anzeigen",

                ["MenuSettings"] = "Systemeinstellungen:",
                ["AlwaysOnTop"] = "📌 Immer im Vordergrund",
                ["AutoQuiet"] = "🌿 Auto-Leise bei Leerlauf (5 Min.)",
                ["Startup"] = "🚀 Mit Windows starten",
                ["TrayIconMenu"] = "🏷️ Dynamisches Tray-Icon",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "GPU %",
                ["TrayMetricRam"] = "RAM %",
                ["TrayMetricNone"] = "Standard-Icon",
                ["TrayMetricNoneShort"] = "Icon",
                ["BtnSpecsShort"] = "ℹ Systemdaten",
                ["Opacity"] = "👁️ Fenster-Transparenz",
                ["Language"] = "🌐 Sprache / Language",
                ["HardwareInfo"] = "ℹ Systeminformationen",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Beenden",

                ["InfoTitle"] = "Hardware-Unterstützung & Systemdaten",
                ["InfoDetected"] = "💻 IM SYSTEM ERKANNT",
                ["InfoSupported"] = "🚀 UNTERSTÜTZTE ARCHITEKTUREN",
                ["InfoClose"] = "Schließen",
                ["SysMemory"] = "Systemspeicher",
                ["DedMemory"] = "Dedizierter Videospeicher",
                ["PhysicalDrives"] = "Physische Laufwerke"
            },
            ["es"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Silencioso",
                ["SilentFull"] = "🌿 Silencioso (99%)",
                ["Boost"] = "Turbo",
                ["BoostActive"] = "⚡ Turbo ACTIVO (100%)",
                ["Threads"] = "Hilos",
                ["ThreadsActive"] = "Hilos activos",
                ["AllDisksIdle"] = "Discos inactivos",
                ["Idle"] = "Inactivo",
                ["ShowAllGpus"] = "Todas las GPU",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DISCO",

                ["TipInfo"] = "Especificaciones del sistema",
                ["TipPinOn"] = "Siempre visible (Fijado)",
                ["TipPinOff"] = "Siempre visible (No fijado)",
                ["TipDetach"] = "Desacoplar widget flotante",
                ["TipDock"] = "Acoplar a bandeja del sistema",
                ["TipClose"] = "Ocultar en la bandeja",

                ["MenuGraphs"] = "Gráficos y módulos:",
                ["CpuGraph"] = "📊 Gráfico de CPU",
                ["CpuCores"] = "🔳 Mapa de núcleos CPU",
                ["TopProcesses"] = "⚡ Top 5 procesos de CPU",
                ["RamGraph"] = "🧠 Gráfico de RAM",
                ["GpuGraph"] = "🎮 Gráfico de GPU",
                ["GpuTemp"] = "🌡️ Curva de temperatura GPU",
                ["GpuFan"] = "💨 Velocidad ventilador GPU",
                ["VramGraph"] = "📼 Gráfico de VRAM",
                ["DiskGraph"] = "💽 Gráfico de Discos (SSD/HDD)",
                ["ShowAllGraphs"] = "Mostrar todos los gráficos",

                ["MenuSettings"] = "Configuración del sistema:",
                ["AlwaysOnTop"] = "📌 Siempre en primer plano",
                ["AutoQuiet"] = "🌿 Silencio automático en reposo (5 min)",
                ["Startup"] = "🚀 Iniciar con Windows",
                ["TrayIconMenu"] = "🏷️ Icono dinámico en bandeja",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "GPU %",
                ["TrayMetricRam"] = "RAM %",
                ["TrayMetricNone"] = "Icono estándar",
                ["TrayMetricNoneShort"] = "Icono",
                ["BtnSpecsShort"] = "ℹ Especificaciones",
                ["Opacity"] = "👁️ Opacidad de ventana",
                ["Language"] = "🌐 Idioma / Language",
                ["HardwareInfo"] = "ℹ Especificaciones del sistema",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Salir",

                ["InfoTitle"] = "Soporte de hardware y especificaciones",
                ["InfoDetected"] = "💻 DETECTADO EN EL SISTEMA",
                ["InfoSupported"] = "🚀 ARQUITECTURAS COMPATIBLES",
                ["InfoClose"] = "Cerrar",
                ["SysMemory"] = "Memoria del sistema",
                ["DedMemory"] = "Memoria de video dedicada",
                ["PhysicalDrives"] = "Discos físicos"
            },
            ["fr"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Silencieux",
                ["SilentFull"] = "🌿 Silencieux (99%)",
                ["Boost"] = "Turbo",
                ["BoostActive"] = "⚡ Turbo ACTIF (100%)",
                ["Threads"] = "Threads",
                ["ThreadsActive"] = "Threads actifs",
                ["AllDisksIdle"] = "Tous disques au repos",
                ["Idle"] = "Repos",
                ["ShowAllGpus"] = "Tous les GPU",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DISQUE",

                ["TipInfo"] = "Spécifications du système",
                ["TipPinOn"] = "Toujours visible (Épinglé)",
                ["TipPinOff"] = "Toujours visible (Non épinglé)",
                ["TipDetach"] = "Détacher le widget flottant",
                ["TipDock"] = "Ancrer dans la barre des tâches",
                ["TipClose"] = "Masquer dans la zone de notification",

                ["MenuGraphs"] = "Graphiques & modules :",
                ["CpuGraph"] = "📊 Graphique CPU",
                ["CpuCores"] = "🔳 Grille des cœurs CPU",
                ["TopProcesses"] = "⚡ Top 5 processus CPU",
                ["RamGraph"] = "🧠 Graphique RAM",
                ["GpuGraph"] = "🎮 Graphique GPU",
                ["GpuTemp"] = "🌡️ Température GPU",
                ["GpuFan"] = "💨 Vitesse ventilateur GPU",
                ["VramGraph"] = "📼 Graphique VRAM",
                ["DiskGraph"] = "💽 Activité des disques (SSD/HDD)",
                ["ShowAllGraphs"] = "Afficher tous les graphiques",

                ["MenuSettings"] = "Paramètres système :",
                ["AlwaysOnTop"] = "📌 Toujours au premier plan",
                ["AutoQuiet"] = "🌿 Mode silencieux en inactivité (5 min)",
                ["Startup"] = "🚀 Lancer au démarrage de Windows",
                ["TrayIconMenu"] = "🏷️ Icône dynamique dans la barre",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "GPU %",
                ["TrayMetricRam"] = "RAM %",
                ["TrayMetricNone"] = "Icône standard",
                ["TrayMetricNoneShort"] = "Icône",
                ["BtnSpecsShort"] = "ℹ Spécifications",
                ["Opacity"] = "👁️ Opacité de la fenêtre",
                ["Language"] = "🌐 Langue / Language",
                ["HardwareInfo"] = "ℹ Spécifications système",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Quitter",

                ["InfoTitle"] = "Support matériel & spécifications",
                ["InfoDetected"] = "💻 DÉTECTÉ SUR VOTRE SYSTÈME",
                ["InfoSupported"] = "🚀 ARCHITECTURES PRISES EN CHARGE",
                ["InfoClose"] = "Fermer",
                ["SysMemory"] = "Mémoire système",
                ["DedMemory"] = "Mémoire vidéo dédiée",
                ["PhysicalDrives"] = "Disques physiques"
            },
            ["ja"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "静音",
                ["SilentFull"] = "🌿 静音モード (99%)",
                ["Boost"] = "ブースト",
                ["BoostActive"] = "⚡ ブースト有効 (100%)",
                ["Threads"] = "スレッド",
                ["ThreadsActive"] = "スレッド稼働中",
                ["AllDisksIdle"] = "全ディスクアイドル中",
                ["Idle"] = "アイドル",
                ["ShowAllGpus"] = "すべてのGPU",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "ディスク",

                ["TipInfo"] = "システム仕様とハードウェア情報",
                ["TipPinOn"] = "常に最前面に表示 (固定)",
                ["TipPinOff"] = "常に最前面に表示 (解除)",
                ["TipDetach"] = "デスクトップウィジェットとして分離",
                ["TipDock"] = "タスクトレイにドック",
                ["TipClose"] = "トレイに最小化",

                ["MenuGraphs"] = "グラフとモジュール:",
                ["CpuGraph"] = "📊 CPU負荷グラフ",
                ["CpuCores"] = "🔳 CPUコア稼働状況",
                ["TopProcesses"] = "⚡ CPU負荷上位5プロセス",
                ["RamGraph"] = "🧠 メモリ使用量グラフ",
                ["GpuGraph"] = "🎮 GPU負荷グラフ",
                ["GpuTemp"] = "🌡️ GPU温度推移線",
                ["GpuFan"] = "💨 GPUファン回転数",
                ["VramGraph"] = "📼 ビデオメモリグラフ",
                ["DiskGraph"] = "💽 ディスクアクセス (SSD/HDD)",
                ["ShowAllGraphs"] = "すべてのグラフを表示",

                ["MenuSettings"] = "システム設定:",
                ["AlwaysOnTop"] = "📌 常に最前面に表示",
                ["AutoQuiet"] = "🌿 アイドル時自動静音 (5分)",
                ["Startup"] = "🚀 Windows起動時に自動実行",
                ["TrayIconMenu"] = "🏷️ トレイアイコンの数値表示",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "GPU %",
                ["TrayMetricRam"] = "RAM %",
                ["TrayMetricNone"] = "標準ステータスアイコン",
                ["TrayMetricNoneShort"] = "アイコン",
                ["BtnSpecsShort"] = "ℹ ハードウェア情報",
                ["Opacity"] = "👁️ ウィンドウの不透明度",
                ["Language"] = "🌐 言語 / Language",
                ["HardwareInfo"] = "ℹ ハードウェア情報",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ 終了",

                ["InfoTitle"] = "ハードウェアサポートと仕様",
                ["InfoDetected"] = "💻 検出されたシステム構成",
                ["InfoSupported"] = "🚀 対応アーキテクチャ",
                ["InfoClose"] = "閉じる",
                ["SysMemory"] = "システムメモリ",
                ["DedMemory"] = "専用ビデオメモリ",
                ["PhysicalDrives"] = "物理ドライブ"
            },
            ["pt"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "Silencioso",
                ["SilentFull"] = "🌿 Silencioso (99%)",
                ["Boost"] = "Turbo",
                ["BoostActive"] = "⚡ Turbo ATIVO (100%)",
                ["Threads"] = "Threads",
                ["ThreadsActive"] = "Threads ativas",
                ["AllDisksIdle"] = "Discos inativos",
                ["Idle"] = "Inativo",
                ["ShowAllGpus"] = "Todas as GPUs",

                ["Cpu"] = "CPU",
                ["Ram"] = "RAM",
                ["Gpu"] = "GPU",
                ["Vram"] = "VRAM",
                ["Disk"] = "DISCO",

                ["TipInfo"] = "Especificações do sistema",
                ["TipPinOn"] = "Sempre no topo (Fixado)",
                ["TipPinOff"] = "Sempre no topo (Desafixado)",
                ["TipDetach"] = "Destacar widget flutuante",
                ["TipDock"] = "Fixar na bandeja",
                ["TipClose"] = "Minimizar para a bandeja",

                ["MenuGraphs"] = "Gráficos e módulos:",
                ["CpuGraph"] = "📊 Gráfico de uso da CPU",
                ["CpuCores"] = "🔳 Matriz de núcleos da CPU",
                ["TopProcesses"] = "⚡ Top 5 processos por CPU",
                ["RamGraph"] = "🧠 Gráfico de uso da RAM",
                ["GpuGraph"] = "🎮 Gráfico de uso da GPU",
                ["GpuTemp"] = "🌡️ Curva de temperatura GPU",
                ["GpuFan"] = "💨 Ventoinhas da GPU",
                ["VramGraph"] = "📼 Gráfico de VRAM",
                ["DiskGraph"] = "💽 Atividade do Disco (SSD/HDD)",
                ["ShowAllGraphs"] = "Mostrar todos os gráficos",

                ["MenuSettings"] = "Configurações do sistema:",
                ["AlwaysOnTop"] = "📌 Sempre no topo",
                ["AutoQuiet"] = "🌿 Modo silencioso em inatividade (5 min)",
                ["Startup"] = "🚀 Iniciar com o Windows",
                ["TrayIconMenu"] = "🏷️ Ícone dinâmico na bandeja",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "GPU %",
                ["TrayMetricRam"] = "RAM %",
                ["TrayMetricNone"] = "Ícone padrão",
                ["TrayMetricNoneShort"] = "Ícone",
                ["BtnSpecsShort"] = "ℹ Especificações",
                ["Opacity"] = "👁️ Opacidade da janela",
                ["Language"] = "🌐 Idioma / Language",
                ["HardwareInfo"] = "ℹ Especificações do sistema",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ Sair",

                ["InfoTitle"] = "Suporte de hardware e especificações",
                ["InfoDetected"] = "💻 DETECTADO NO SISTEMA",
                ["InfoSupported"] = "🚀 ARQUITETURAS SUPORTADAS",
                ["InfoClose"] = "Fechar",
                ["SysMemory"] = "Memória do sistema",
                ["DedMemory"] = "Memória de vídeo dedicada",
                ["PhysicalDrives"] = "Discos físicos"
            },
            ["zh"] = new()
            {
                ["AppTitle"] = "RyzenQuiet PRO v3.0",
                ["Silent"] = "静音",
                ["SilentFull"] = "🌿 静音模式 (99%)",
                ["Boost"] = "加速",
                ["BoostActive"] = "⚡ 加速开启 (100%)",
                ["Threads"] = "线程",
                ["ThreadsActive"] = "线程运行中",
                ["AllDisksIdle"] = "全部磁盘闲置",
                ["Idle"] = "空闲",
                ["ShowAllGpus"] = "所有显卡",

                ["Cpu"] = "CPU",
                ["Ram"] = "内存",
                ["Gpu"] = "显卡",
                ["Vram"] = "显存",
                ["Disk"] = "磁盘",

                ["TipInfo"] = "系统规格与硬件支持",
                ["TipPinOn"] = "窗口置顶 (已固定)",
                ["TipPinOff"] = "窗口置顶 (已取消)",
                ["TipDetach"] = "分离为桌面悬浮小部件",
                ["TipDock"] = "停靠至系统托盘",
                ["TipClose"] = "最小化到托盘",

                ["MenuGraphs"] = "图表与模块:",
                ["CpuGraph"] = "📊 CPU 负载图表",
                ["CpuCores"] = "🔳 CPU 核心热力图",
                ["TopProcesses"] = "⚡ CPU 占用前 5 进程",
                ["RamGraph"] = "🧠 内存占用图表",
                ["GpuGraph"] = "🎮 显卡负载图表",
                ["GpuTemp"] = "🌡️ 显卡温度曲线",
                ["GpuFan"] = "💨 显卡风扇转速",
                ["VramGraph"] = "📼 显存占用图表",
                ["DiskGraph"] = "💽 磁盘读写活动 (SSD/HDD)",
                ["ShowAllGraphs"] = "显示所有图表",

                ["MenuSettings"] = "系统设置:",
                ["AlwaysOnTop"] = "📌 窗口总在最前",
                ["AutoQuiet"] = "🌿 空闲自动静音 (5分钟)",
                ["Startup"] = "🚀 随 Windows 开机启动",
                ["TrayIconMenu"] = "🏷️ 托盘动态图标",
                ["TrayMetricCpu"] = "CPU %",
                ["TrayMetricGpu"] = "显卡 %",
                ["TrayMetricRam"] = "内存 %",
                ["TrayMetricNone"] = "标准状态图标",
                ["TrayMetricNoneShort"] = "标准图标",
                ["BtnSpecsShort"] = "ℹ 硬件规格",
                ["Opacity"] = "👁️ 窗口不透明度",
                ["Language"] = "🌐 语言 / Language",
                ["HardwareInfo"] = "ℹ 硬件规格信息",
                ["ToolsLink"] = "https://ph-cu-s.com/tools",
                ["Exit"] = "✕ 退出",

                ["InfoTitle"] = "硬件支持与系统规格",
                ["InfoDetected"] = "💻 系统检测到的硬件",
                ["InfoSupported"] = "🚀 支持的硬件架构",
                ["InfoClose"] = "关闭",
                ["SysMemory"] = "系统内存",
                ["DedMemory"] = "独立显存",
                ["PhysicalDrives"] = "物理磁盘数量"
            }
        };

        public static void Initialize(string configuredLang)
        {
            if (string.IsNullOrWhiteSpace(configuredLang) || configuredLang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                string sysCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (Strings.ContainsKey(sysCode))
                {
                    _currentLang = sysCode;
                }
                else
                {
                    _currentLang = "en";
                }
            }
            else if (Strings.ContainsKey(configuredLang.ToLowerInvariant()))
            {
                _currentLang = configuredLang.ToLowerInvariant();
            }
            else
            {
                _currentLang = "en";
            }
        }

        public static void SetLanguage(string langCode)
        {
            if (Strings.ContainsKey(langCode) && _currentLang != langCode)
            {
                _currentLang = langCode;
                LanguageChanged?.Invoke();
            }
        }

        public static string Get(string key)
        {
            if (Strings.TryGetValue(_currentLang, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }
            if (Strings.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }
            return key;
        }
    }
}
