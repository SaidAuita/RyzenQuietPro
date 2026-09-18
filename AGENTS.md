# RyzenQuiet PRO Rules

Родительский контекст: C:\_CODE\AGENTS.md  
Проект: RyzenQuiet PRO (C# .NET 8 WinForms Hardware Monitor & Acoustic Silencer)

---

## 1. Общие правила проекта
- Всегда отвечать пользователю **на русском языке** (Always respond to the user in Russian).
- Следовать принципу **CONTINUE, DON'T RESTART**: перед началом работы читать `PROJECT_STATUS.md`.

## 2. Управление ветками Git
- **Единственная рабочая ветка**: вся разработка и релизы ведутся строго в ветке **`v4`**.
- Все промежуточные или экспериментальные ветки удаляются после завершения итерации. Актуальная рабочая ветка всегда `v4` (`origin/v4`).

## 3. Версионирование при итерациях
- При **каждой новой итерации** обязателен инкремент версии релиза: `4.01`, `4.02`, `4.03` и т.д.
- Версия синхронизируется во всех ключевых файлах проекта:
  1. `RyzenQuietPro.csproj` (`<Version>4.01.0</Version>`, `<AssemblyVersion>4.01.0.0</AssemblyVersion>`, `<FileVersion>4.01.0.0</FileVersion>`, `<InformationalVersion>v4.01 PRO</InformationalVersion>`)
  2. `RyzenQuiet.FanService/RyzenQuiet.FanService.csproj`
  3. `build.bat` (`set VERSION=v4.01`)
  4. `Localization.cs` (во всех языковых словарях: `["AppTitle"] = "RyzenQuiet PRO v4.01"`)
  5. `DashboardPro.cs` (`Text = "RyzenQuiet PRO v4.01"`)
  6. `TrayApp.cs` (`ToolStripMenuItem("RyzenQuiet PRO v4.01")`)
  7. `README.md` (в заголовках, бейджах и ссылках)
- После завершения задачи обновлять `PROJECT_STATUS.md`.
