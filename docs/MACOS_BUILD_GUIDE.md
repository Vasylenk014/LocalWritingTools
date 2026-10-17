# WritingTools for macOS — Architecture, Build Guide & Re-deployment Manual

Цей документ містить повний архітектурний опис, технічну специфікацію та **покрокову інструкцію для збирання й запуску WritingTools на будь-якому іншому комп'ютері Mac** (macOS 14 Sonoma, macOS 15 Sequoia і новіше).

---

## Частина 1: ГОТОВИЙ MASTER PROMPT (для Antigravity на новому Mac)

Якщо ви відкрили нову сесію Antigravity на іншому Mac і маєте розпаковану папку з бекапом або репозиторієм, просто скопіюйте цей промпт:

```markdown
У мене є файли нативного macOS застосунку WritingTools (папка Sources з WritingToolsApp.swift, ViewModel.swift, ContentView.swift, TextDiff.swift, HotKey.swift, файл AppIcon.icns та скрипт build.sh).

Допоможи мені:
1. Перевірити наявність необхідних інструментів розробки (Xcode Command Line Tools / swift).
2. Запустити скрипт ./build.sh для компіляції релізної збірки WritingTools.app.
3. Встановити застосунок у /Applications/WritingTools.app, очистити розширені атрибути карантину (xattr -cr).
4. Запустити застосунок і переконатися, що він з'явився у Menu Bar.
```

---

## Частина 2: Швидка збірка вручну через Термінал (Manual CLI Build)

Якщо ви хочете зібрати застосунок власноруч без штучного інтелекту:

### 1. Системні вимоги:
- macOS 14.0 (Sonoma) або новіша (Apple Silicon M1/M2/M3/M4 або Intel).
- Встановлені **Xcode Command Line Tools** (якщо ще не встановлені, виконайте в терміналі: `xcode-select --install`).
- Встановлений [LM Studio](https://lmstudio.ai/) із запущеним локальним сервером на порту 1234 (`Developer` → `Start Server`).

### 2. Команди для збірки та встановлення:

Відкрийте **Terminal.app** і виконайте команди:

```bash
# 1. Перейдіть у папку з кодом проєкту
cd /шлях/до/папки/WritingTools

# 2. Надайте права на виконання скрипту збірки
chmod +x build.sh

# 3. Запустіть релізну збірку
./build.sh

# 4. Закрийте попередній екземпляр (якщо був запущений)
pkill -x WritingTools 2>/dev/null || true

# 5. Встановіть у системну папку Applications
rm -rf /Applications/WritingTools.app
cp -R WritingTools.app /Applications/

# 6. Зніміть мітку карантину macOS Gatekeeper
xattr -cr /Applications/WritingTools.app
touch /Applications/WritingTools.app

# 7. Запустіть застосунок
open /Applications/WritingTools.app
```

Після виконання іконка WritingTools (перо з галочкою) з'явиться праворуч у Menu Bar macOS біля годинника.

---

## Частина 3: Архітектура та структура проєкту

```
WritingTools/
├── AppIcon.icns                # Готова multi-resolution іконка застосунку (16x16 до 1024x1024)
├── build.sh                    # Автономний скрипт збірки бандла
├── Package.swift               # Маніфест Swift Package Manager (генерується build.sh)
└── Sources/
    ├── WritingToolsApp.swift   # Точка входу: NSApplication, NSStatusItem, NSPopover, хоткей, меню Edit + Transformations
    ├── ViewModel.swift         # Бізнес-логіка, клієнт LM Studio v1, стрімінг SSE, промпти, історія
    ├── ContentView.swift       # Графічний інтерфейс SwiftUI (popover)
    ├── TextDiff.swift          # Алгоритм по-токенового порівняння тексту (Visual Diff)
    ├── TextTransformation.swift# Детерміновані миттєві трансформації тексту без ШІ
    └── HotKey.swift            # Обертка для реєстрації глобальної комбінації клавіш через Carbon
```

### Детальний опис компонентів:

### 1. `WritingToolsApp.swift` (Точка входу та інтеграція з macOS)
- **Чому `AppDelegate.main()` замість `@main struct App: SwiftUI.App`:**
  У чистому SwiftUI для менюбар-додатків часто відкривається порожнє фантомне вікно (Settings scene), а при закритті вікна менюбар-іконка може безслідно зникати. Використання класичного AppKit `NSApplicationDelegate` із політикою активації `NSApp.setActivationPolicy(.accessory)` гарантує:
  1. Застосунок живе тільки в Menu Bar.
  2. Не має іконки в Dock.
  3. Не відкриває жодних зайвих вікон при старті.
- **`NSPopover` із dynamic sizing:**
  Використовує `NSHostingController` із `sizingOptions = [.preferredContentSize]`. Завдяки цьому розмір вікна плавно й автоматично підлаштовується під висоту контенту SwiftUI (наприклад, при розгортанні блоків промпта чи контексту).
- **Нативне меню Edit (`⌘V`, `⌘C`, `⌘A`, `⌘Z`):**
  У додатках типу `.accessory` без власного верхнього рядка меню стандартні системні шорткати вставки/копіювання часто блокуються. У коді реалізовано програмне створення `NSMenu` з діями `cut:`, `copy:`, `paste:`, `undo:`, `redo:`, `selectAll:`, що забезпечує бездоганну роботу `⌘V` у всіх текстових полях.
- **Глобальний хоткей `⌥⌘G`:**
  Використовує Carbon EventHotKey. При натисканні комбінації з будь-якої програми бере поточний виділений текст із буфера обміну, показує попап і запускає покращення.
- **Transient behavior:**
  Глобальний монітор подій миші закриває попап, коли користувач клікає у будь-яке інше місце екрана.

### 2. `ViewModel.swift` (Ядро та робота з LM Studio)
- **API клієнт:**
  Працює безпосередньо з сучасним нативним REST API LM Studio (`http://127.0.0.1:1234/api/v1`):
  - `GET /api/v1/models` — отримання списку моделей, статусу завантаження в RAM/VRAM (`●`), та доступних режимів Reasoning.
  - `POST /api/v1/chat` — стрімінг у реальному часі через `URLSession.shared.bytes.lines` (SSE).
  - Усі запити мають параметр `"store": false`, що забезпечує повну безстатусність (stateless) — пам'ять моделі не засмічується старими чернетками.
- **3 Режими роботи:**
  - **Quick fix** (швидке виправлення граматики зі збереженням тону та довжини).
  - **Detailed review** (структурований розбір: `CORRECTED`, `CONCISE`, `CHANGES`, `NOTES`).
  - **Summarize** (стислий вижим довгих листів клієнтів: Core Issue, Key Details, Customer Goal).
- **Тональні пресети:**
  П'ять тональностей для Quick fix (`Standard`, `Friendly`, `Concise`, `De-escalate`, `Formal`).
- **Опціональний контекст тікета (`Ticket Context`):**
  Передає історію листування або вхідний лист клієнта у тегах `<context>...</context>` перед чернеткою `<draft>...</draft>`. Моделі суворо проінструктовані переписувати тільки `<draft>`, використовуючи `<context>` виключно як довідник.
- **Історія (Recent Checks):**
  Зберігає останні 15 перевірок у `UserDefaults` з можливістю відновлення (Restore) або швидкого копіювання (Copy).
- **Очищення результату (`clean`):**
  Видаляє артефакти мислення моделей (`<think>...</think>`), випадкові теги `<draft>` / `<context>`, зайві лапки та markdown-блоки коду.

### 3. `ContentView.swift` (Інтерфейс)
- Повністю адаптивний дизайн із фіксованою комфортною шириною (470px).
- Індикатор статусу сервера (зелена/червона крапка).
- Керування Reasoning (Auto / Off / Low / Medium / High / On).
- Підтримка перемикача режиму Text / Diff.
- Нативний Markdown-рендеринг для детального огляду та саммарі.

### 4. `TextDiff.swift` (Word-level Visual Diff)
- Розбиває вхідний і виправлений тексти на токени (слова, розділові знаки, пробіли).
- За алгоритмом Longest Common Subsequence (LCS) обчислює точні відмінності.
- Зеленим підсвічує додані фрагменти, червоним із закресленням — видалені.

### 5. `TextTransformation.swift` (Миттєві трансформації без ШІ)
- **Детерміновані текстові операції (0 ms, без виклику LLM):**
  - `Make Upper Case`: переведення всього тексту у верхній регістр.
  - `Make Lower Case`: переведення всього тексту у нижній регістр.
  - `Capitalize`: кожне слово з великої літери.
  - `Sentence case`: розумна капіталізація перших літер речень (після `.`, `!`, `?` та переносу рядка), із коректною обробкою суцільного CAPS.
  - `Clean Extra Spaces`: видалення подвійних пробілів, зайвих відступів та колапс пустих рядків.
  - `Straighten Quotes`: заміна типографських лапок («», “”) на прямі (`"`, `'`).
  - `Remove Line Breaks`: розгортання одинарних переносів рядків у суцільний абзац зі збереженням подвійних переносів.
- Доступні як через кнопку **Transform ▾** у шапці Draft, так і через стандартне меню **Edit → Transformations** (правою кнопкою миші на виділеному фрагменті).

### 6. `build.sh` (Збірка самодостатнього `.app` бандла)
- Генерує `Package.swift` із таргетом для macOS 14.
- Викликає `swift build -c release`.
- Створює структуру macOS бандла:
  - `WritingTools.app/Contents/MacOS/WritingTools`
  - `WritingTools.app/Contents/Resources/AppIcon.icns`
  - `WritingTools.app/Contents/Info.plist`
- В `Info.plist` прописує ключ `<key>LSUIElement</key><true/>`, що робить застосунок системним Menu Bar агентом.

---

## Частина 4: Налаштування на новому Mac

### 1. Налаштування LM Studio:
1. Завантажте та встановіть LM Studio з [lmstudio.ai](https://lmstudio.ai/).
2. Перейдіть на вкладку **Developer** (значок `<>` зліва внизу).
3. Увімкніть **Start Server** (порт за замовчуванням `1234`).
4. Завантажте будь-яку зручну модель (рекомендовано: `Qwen 2.5 7B/14B Instruct`, `Gemma 2 9B`, `Llama 3.1 8B` або `Mistral Nemo`).

### 2. Дозволи системи (System Permissions):
- **Спеціальні можливості (Accessibility):**
  Для роботи глобального хоткею `⌥⌘G` macOS може попросити дозвіл на доступ до функцій керування:
  - Відкрийте **System Settings** → **Privacy & Security** → **Accessibility**.
  - Переконайтеся, що перемикач біля `WritingTools` увімкнено.
- **Автозапуск при старті системи (опціонально):**
  - Відкрийте **System Settings** → **General** → **Login Items**.
  - Натисніть `+` і додайте `/Applications/WritingTools.app`.

---

## Частина 5: Можливі проблеми та їх вирішення (Troubleshooting)

1. **Помилка під час збірки: `failed to build module 'Swift'; this SDK is not supported by the compiler`**
   - **Причина:** Невідповідність кешу Command Line Tools після оновлення macOS.
   - **Рішення:** Виконайте в терміналі:
     ```bash
     sudo xcode-select --reset
     ```
     і повторіть `./build.sh`.

2. **Застосунок не відкривається через Gatekeeper («Застосунок пошкоджено» або «Розробника не перевірено»)**
   - **Причина:** Захисний карантинний атрибут `com.apple.quarantine` для файлів, завантажених з мережі / iCloud.
   - **Рішення:**
     ```bash
     xattr -cr /Applications/WritingTools.app
     ```

3. **Червона крапка (LM Studio unreachable)**
   - Перевірте, чи запущено сервер у LM Studio (`Developer` → `Start Server`).
   - Перевірте, чи в налаштуваннях LM Studio сервер слухає порт `1234` і чи адреса `http://127.0.0.1:1234`.
   - Натисніть кнопку оновлення (🔄) біля вибору моделі у вікні WritingTools.
