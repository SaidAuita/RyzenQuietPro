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

## 4. Регламент сборки и публикации релиза (GitHub и сайт ph-cu-s.com)

При каждом релизе новой версии строго следовать протоколу публикации:

### Шаг 1. Сборка бинарников
1. Запустить скрипт `build.bat` в корне проекта (`c:\_CODE\Utilites\RyzenQuietPro\build.bat`).
2. Проверить генерацию файлов в папке `build\`:
   - `build\RyzenQuietPro-vX.XX.exe` (Standalone, ~70-75 МБ)
   - `build\RyzenQuietPro-vX.XX-Lite.exe` (Lite, ~1.8 МБ)
   - `build\plugins\FanService\RyzenQuiet.FanService.exe` (Fan Monitor Addon, ~5.4 МБ)

### Шаг 2. Фиксация и публикация в репозитории GitHub
1. Добавить скриншоты новой версии в `images/` (например, `images/RyzenQuietPro-v4_15-1.png`, `images/RyzenQuietPro-v4_15-2.png`).
2. Актуализировать `README.md` на 2 языках (EN и RU) со ссылками на новые изображения и описанием изменений.
3. Зафиксировать изменения в Git и отправить в ветку `v4`:
   ```bash
   cd c:\_CODE\Utilites\RyzenQuietPro
   git add -A
   git commit -m "feat: release vX.XX - <краткое описание>"
   git push origin v4
   ```

### Шаг 3. Подготовка и обновление файлов сайта ph-cu-s.com
Рабочая локальная копия сайта: `C:\xampp\htdocs\www.ph-cu-s.com\`

1. **Копирование дистрибутивов и скриншотов в статику сайта**:
   - Бинарники скопировать в: `C:\xampp\htdocs\www.ph-cu-s.com\server\static\tools\`
     - `RyzenQuietPro-vX.XX.exe`
     - `RyzenQuietPro-vX.XX-Lite.exe`
   - Скриншоты скопировать в: `C:\xampp\htdocs\www.ph-cu-s.com\server\static\img\tools\`
     - `RyzenQuietPro-vX_XX-1.png`
     - `RyzenQuietPro-vX_XX-2.png`

2. **Обновление веб-шаблонов сайта на 2 языках**:
   - Каталог утилит (EN): `server\templates\tools\index.html` (бейдж `vX.XX`, описание, превью-картинка)
   - Каталог утилит (RU): `server\templates\ru\tools\index.html` (бейдж `vX.XX`, описание, превью-картинка)
   - Страница утилиты (EN): `server\templates\tools\ryzenquietpro.html`
     - `<title>`, заголовок `RyzenQuiet PRO (vX.XX)`, бейджи
     - Ссылки на скачивание Standalone и Lite версий
     - Карточка `📝 Release Notes (vX.XX Changelog)`
     - Слайдер изображений `slides` (добавить новые скриншоты в начало массива)
     - Список ключевых возможностей (`Key Features and Capabilities`)
   - Страница утилиты (RU): `server\templates\ru\tools\ryzenquietpro.html`
     - `<title>`, заголовок `RyzenQuiet PRO (vX.XX)`, бейджи
     - Ссылки на скачивание Standalone и Lite версий
     - Карточка `📝 Примечания к релизу vX.XX (Что нового)`
     - Слайдер изображений `slides` (добавить новые скриншоты в начало массива)
     - Список ключевых возможностей (`Ключевые возможности`)

### Шаг 4. Деплой на боевой сервер (phcus)
Боевой сервер доступен по SSH-алиасу `phcus` (путь `/var/www/html/`).

1. **Загрузка бинарников и изображений**:
   ```bash
   scp C:\_CODE\Utilites\RyzenQuietPro\build\RyzenQuietPro-vX.XX*.exe phcus:/var/www/html/static/tools/
   scp C:\_CODE\Utilites\RyzenQuietPro\images\RyzenQuietPro-vX_XX*.png phcus:/var/www/html/static/img/tools/
   ```

2. **Загрузка обновленных шаблонов**:
   ```bash
   scp C:\xampp\htdocs\www.ph-cu-s.com\server\templates\tools\index.html phcus:/var/www/html/templates/tools/
   scp C:\xampp\htdocs\www.ph-cu-s.com\server\templates\tools\ryzenquietpro.html phcus:/var/www/html/templates/tools/
   scp C:\xampp\htdocs\www.ph-cu-s.com\server\templates\ru\tools\index.html phcus:/var/www/html/templates/ru/tools/
   scp C:\xampp\htdocs\www.ph-cu-s.com\server\templates\ru\tools\ryzenquietpro.html phcus:/var/www/html/templates/ru/tools/
   ```

3. **Установка прав доступа и перезагрузка веб-сервера gunicorn**:
   ```bash
   ssh phcus "chmod 664 /var/www/html/static/tools/RyzenQuietPro-vX.XX* /var/www/html/static/img/tools/RyzenQuietPro-vX_XX* /var/www/html/templates/tools/index.html /var/www/html/templates/tools/ryzenquietpro.html /var/www/html/templates/ru/tools/index.html /var/www/html/templates/ru/tools/ryzenquietpro.html && pkill -HUP -f gunicorn"
   ```

4. **Проверка боевых страниц**:
   ```bash
   curl -s -o /dev/null -w "%{http_code}\n" https://ph-cu-s.com/tools
   curl -s -o /dev/null -w "%{http_code}\n" https://ph-cu-s.com/ru/tools
   curl -s -o /dev/null -w "%{http_code}\n" https://ph-cu-s.com/tools/ryzenquietpro
   curl -s -o /dev/null -w "%{http_code}\n" https://ph-cu-s.com/ru/tools/ryzenquietpro
   ```
   Все ответы должны возвращать `200`.


