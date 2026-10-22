# Local Writing Tools for Windows

Нативна версія **Local Writing Tools** для Windows 11 / 10, розроблена на **C# .NET 8 (WPF)** із повною підтримкою локального апаратного прискорення на **NPU (AMD Ryzen AI)** та **GPU (через LM Studio)**.

---

## Основні можливості

- **Універсальний AI-провайдер**:
  - **⚡ FastFlowLM (NPU)**: робота безпосередньо з AMD Ryzen AI NPU на ноутбуці (порт `52625`), нульове навантаження на GPU, економія заряду батареї.
  - **🎮 LM Studio (GPU)**: підключення до локального сервера LM Studio на стаціонарному ПК (порт `1234`), повна підтримка моделей DeepSeek, Qwen, Llama.
  - **🌐 Custom Server**: можливість підключення до Ollama (`:11434`) або будь-якого OpenAI-сумісного сервера.
- **Швидке перемикання**: перемикання між NPU та GPU в 1 клік прямо з заголовка вікна (`[⚡ NPU ▾]` / `[🎮 LM Studio ▾]`).
- **OLED Dark Theme**: глибока чорна тема з нативними атрибутами Windows 11 DWM (Immersive Dark Mode, заокруглені кути).
- **Жива системна телеметрія**: моніторинг фізичної оперативної пам'яті (RAM) та апаратного завантаження (NPU compute engine або GPU 3D/Compute).
- **Smart Copy**: розумне копіювання (для *Detailed review* автоматично копіює лише виправлений блок `CORRECTED`, відсікаючи службові коментарі).
- **Інтерактивний Diff View**: підсвічування видалених (червоний) та доданих (зелений) слів.
- **Глобальний шорткат**: `Alt+Shift+G` для швидкої обробки виділеного тексту.
- **Ізоляція буферів**: окремий буфер для вкладки `Summarize` від робочого чернеткового тексту `Quick fix` / `Detailed review`.
- **Редактор системних промптів**: налаштування промптів для кожного режиму з можливістю повернення до заводських налаштувань.

---

## Структура проекту

```text
windows/
├── App.xaml / App.xaml.cs         # Точка входу WPF та ініціалізація трею
├── MainWindow.xaml / .xaml.cs    # Головне вікно, UI-контроли, стрімінг, налаштування
├── WritingTools.csproj            # Проект .NET 8 (WPF + WinForms для NotifyIcon)
├── Models/                        # Моделі даних (AppSettings, FlmModel, HistoryItem тощо)
├── Services/                      # Бізнес-логіка (API, Diff, Hotkey, Metrics, SmartCopy)
└── tests/                         # Набір автоматизованих верифікаційних тестів (19 тестів)
```

---

## Збірка та запуск

### Вимоги
- Windows 10 (версія 19041+) або Windows 11
- .NET 8.0 SDK

### Швидкий запуск
```powershell
dotnet run
```

### Запуск верифікаційних тестів
```powershell
dotnet run --project tests/WritingTools.Tests.csproj
```

### Створення автономного EXE (без потреби встановлювати .NET)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-standalone
```
Готовий файл з'явиться у папці `publish-standalone/WritingTools.exe`.
