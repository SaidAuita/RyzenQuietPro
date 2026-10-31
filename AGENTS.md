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

## 3. Обязательное версионирование при любых изменениях и внедрении новых функций
- **Строгое правило инкремента версии**: при **каждом внедрении новых функций, изменении UI/логики или итерации доработок** ОБЯЗАТЕЛЕН инкремент версии релиза (`4.08` -> `4.09`, `4.10` и т.д.). Никогда не оставлять версию неизменной при появлении новых возможностей.
- Версия синхронизируется синхронно во всех ключевых файлах проекта:
  1. `RyzenQuietPro.csproj` (`<Version>4.10.0</Version>`, `<AssemblyVersion>4.10.0.0</AssemblyVersion>`, `<FileVersion>4.10.0.0</FileVersion>`, `<InformationalVersion>v4.10 PRO</InformationalVersion>`)
  2. `RyzenQuiet.FanService/RyzenQuiet.FanService.csproj`
  3. `build.bat` (`set VERSION=v4.10`)
  4. `Localization.cs` (во всех 8 языковых словарях: `["AppTitle"] = "RyzenQuiet PRO v4.10"`)
  5. `DashboardPro.cs` (`Text = "RyzenQuiet PRO v4.10"`, заголовок окна и метаданные)
  6. `TrayApp.cs` (`ToolStripMenuItem("RyzenQuiet PRO v4.10")`)
  7. `README.md` (в заголовках, бейджах релиза, ссылках на скачивание и описании новых функций)
- После завершения задачи обязательно обновлять `PROJECT_STATUS.md` с детальным описанием добавленного функционала.

