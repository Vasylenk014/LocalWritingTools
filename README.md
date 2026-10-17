# WritingTools ✍️

> Локальний автономний помічник для перевірки та покращення чернеток відповідей у Zendesk за допомогою локальних LLM-моделей через **LM Studio** (без відправки даних у хмару).

---

## 🌟 Основні можливості

- **Повна конфіденційність:** працює виключно з локальними моделями через нативний REST API LM Studio (`http://127.0.0.1:1234/api/v1`). Кожен запит повністю безстатусний (`"store": false`), щоб не засмічувати пам'ять моделі.
- **Швидкий виклик:**
  - **macOS:** живе у Menu Bar біля годинника, глобальний хоткей `⌥⌘G`.
  - **Windows:** живе у System Tray, глобальний хоткей `Alt+Shift+G` (або `Ctrl+Alt+G`).
- **3 Режими роботи:**
  - **⚡️ Quick fix:** швидке граматичне й стилістичне покращення тексту зі збереженням тону та довжини.
  - **🔍 Detailed review:** глибокий структурований розбір (`CORRECTED`, `CONCISE`, `CHANGES`, `NOTES`).
  - **📝 Summarize:** стислий вижим довгих багатоабзацних листів клієнтів (Core Issue, Key Details, Customer Goal).
- **Опціональний контекст тікета (Ticket context):** передача історії листування або запиту клієнта у тегах `<context>`, щоб модель враховувала суть справи, але переписувала виключно чернетку `<draft>`.
- **Тональні пресети (Quick fix):** `✨ Standard`, `🤝 Friendly`, `⚡️ Concise`, `🛡️ De-escalate`, `👔 Formal`.
- **Миттєві трансформації без ШІ (0 ms):** Make Upper Case, Make Lower Case, Capitalize Words, Sentence case, Clean Extra Spaces, Straighten Quotes, Remove Line Breaks.
- **Word-level Visual Diff:** токен-рівневе підсвічування різниці між оригіналом і результатом (LCS алгоритм: додане зеленим, видалене червоним із закресленням).
- **Історія (Recent Checks):** збереження останніх 15 перевірок з можливістю відновлення (Restore) або копіювання.

---

## 📂 Структура репозиторію

```text
WritingTools/
├── macos/                     # Нативна версія для macOS (Swift, SwiftUI, AppKit)
│   ├── Sources/               # Вихідний код
│   ├── build.sh               # Автономний скрипт збірки бандла .app
│   ├── AppIcon.icns           # Іконка застосунку
│   └── Package.swift          # SPM маніфест
├── windows/                   # Версія для Windows (WPF C# / Python)
│   └── README.md              # Інструкції та вихідні файли Windows
├── docs/                      # Повна технічна документація
│   ├── MACOS_BUILD_GUIDE.md   # Детальний посібник зі збирання на macOS
│   └── WINDOWS_PORT_SPEC.md   # Архітектурна специфікація та Master Prompt для Windows
├── .gitignore
└── README.md
```

---

## 🚀 Швидкий старт

### 1. Налаштування LM Studio (однаково для Mac та Windows):
1. Завантажте [LM Studio](https://lmstudio.ai/).
2. Перейдіть на вкладку **Developer** (`<>`) та натисніть **Start Server** (порт `1234`).
3. Завантажте будь-яку швидку модель (рекомендовано: `Qwen 2.5 7B/14B Instruct`, `Gemma 2 9B`, `Llama 3.1 8B` або `Mistral Nemo`).

### 2. Збірка на macOS:
```bash
cd macos
chmod +x build.sh
./build.sh

# Встановлення в Applications
cp -R WritingTools.app /Applications/
xattr -cr /Applications/WritingTools.app
open /Applications/WritingTools.app
```
Детальніше дивіться у [docs/MACOS_BUILD_GUIDE.md](docs/MACOS_BUILD_GUIDE.md).

### 3. Запуск на Windows:
Дивіться інструкції у [windows/README.md](windows/README.md) та специфікацію у [docs/WINDOWS_PORT_SPEC.md](docs/WINDOWS_PORT_SPEC.md).

---

## 📄 Ліцензія
Personal Support Productivity Tool.
