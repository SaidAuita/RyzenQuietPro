# PROJECT_STATUS: RyzenQuiet PRO

## Текущая версия: v4.01
- **Активная ветка Git**: `v4`
- **Предыдущий релиз**: v4.0 (Stopwatch module, GPU acoustic/power limit tuning, fan service demo hints, resizable settings)

---

## Текущая итерация: v4.01
- [x] Очистка веток Git: удалены устаревшие локальные ветки `archive/v2.x`, `macos`, `main`. Актуализирована ветка `v4`.
- [x] Фиксация правил проекта в `AGENTS.md` (ветка `v4`, инкремент версий `4.01`, `4.02`, ...).
- [x] Добавление ссылки на `System.Management` в `RyzenQuietPro.csproj`.
- [x] Реализация расширенного мониторинга дисков в `DiskMonitor.cs` (WMI связь physical->logical, per-disk PerformanceCounters, history, цвет, модели/производители).
- [x] Добавление настроек `EnhancedDiskMode`, `DiskLegendExpanded`, `HiddenDiskIds` в `AppSettings.cs`.
- [x] Добавление чекбокса «Расширенный режим дисков» в `SettingsForm.cs`.
- [x] Реализация интерактивной легенды дисков со стрелкой сворачивания `▼`/`▲`, переключателями видимости графиков и многоцветной отрисовкой в `DashboardPro.cs`.
- [x] Динамический расчёт высоты окна под реальное число дисков (`driveCount * 22 + 4`) и отключение полосы прокрутки в `DiskLegendPanel`.
- [x] Исправление удержания окна при PIN (`!_alwaysOnTop` в `OnWindowDeactivated`).
- [x] Очистка заголовка секции дисков от неинформативного среднего процента `_lblDisk.Text = Loc.Get("Disk")` и исправление дублирования двоеточия `F::` -> `F:`.
- [x] Архитектурное исследование механизма выявления топ-процессов дискового ввода-вывода через `NtQuerySystemInformation`.
- [x] Обновление локализаций в `Localization.cs` и поднятие версии до v4.01 во всех ключевых файлах.
