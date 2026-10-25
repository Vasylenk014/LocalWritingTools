# Local Writing Tools ✍️

[![Platform: macOS | Windows](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows-brightgreen.svg)](#-repository-structure-monorepo)
[![Privacy: 100% Local](https://img.shields.io/badge/Privacy-100%25%20Local-success.svg)](#-privacy--local-security)
[![Engine: LM Studio & FastFlowLM](https://img.shields.io/badge/Engine-LM%20Studio%20GPU%20%7C%20FastFlowLM%20NPU-orange.svg)](#-getting-started)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](#-license)

> **Local Writing Tools** is a private, lightweight writing companion designed specifically for customer support specialists and professional communications. It connects to locally hosted LLMs via **FastFlowLM (AMD Ryzen AI NPU)** on laptops or **LM Studio (NVIDIA/AMD GPU)** on desktop PCs to refine, edit, and summarize drafts instantly with **zero cloud dependencies**, zero subscription fees, and no risk of customer data leaks.

---

## 🔒 Why Local Writing Tools?

Customer support teams handle sensitive customer information, ticket threads, order numbers, and personal details daily. Pasting customer communications into cloud-based AI tools (OpenAI, Anthropic, etc.) creates compliance risks and compromises customer privacy.

**Local Writing Tools** solves this by:
- Running **100% on your local machine** using local models via FastFlowLM (NPU) or LM Studio (GPU).
- Using **stateless REST requests** (`"store": false`) so ticket context is never retained or indexed into model memory.
- Operating unobtrusively in your **macOS Menu Bar** or **Windows System Tray** with quick global hotkeys.

---

## ✨ Features

- ⚡️ **Global Hotkey Workflow:** Select any text anywhere (support portals, email, browser), press `⌥⌘G` (macOS) or `Alt+Shift+G` (Windows), and the app automatically grabs your clipboard, generates an improved response, and copies it back to your clipboard (`Auto-copy`).
- 🛠️ **3 Purpose-Built Support Modes:**
  - **Quick Fix:** Grammatical correction, clarity, and tone improvements while strictly preserving original meaning, length, facts, and dynamic placeholders (`{{customer.first_name}}`, `{{ticket_id}}`).
  - **Detailed Review:** Structured feedback broken down into: `CORRECTED`, `CONCISE`, `CHANGES` (with grammar/style rationale), and `NOTES`.
  - **Summarize:** Extracts core issues, key details, and the customer's goal from lengthy, AI-generated customer messages in 5 seconds (with buffer isolation from reply drafts).
- 🔀 **Universal Multi-Provider Architecture (Windows):**
  - **⚡ FastFlowLM (NPU):** AMD Ryzen AI NPU on laptop (port `52625`), zero GPU load, highly battery efficient.
  - **🎮 LM Studio (GPU):** Desktop PC with GPU server on port `1234` (supports DeepSeek, Qwen, Llama).
  - **🌐 Custom / Ollama:** Standard OpenAI-compatible server endpoint support.
  - **1-Click Switching:** Instant provider switcher pill button in header (`[⚡ NPU ▾]` / `[🎮 LM Studio ▾]`).
- 💬 **Ticket Context / Thread History:** An optional collapsible context section (`Ticket context`). Pass previous messages or the client's inquiry inside `<context>...</context>` so the model tailors its tone and answers specific questions while rewriting **only** your draft.
- 🎭 **Tone Presets (Quick Fix):** Instant tone modifiers: `✨ Standard`, `🤝 Friendly`, `⚡️ Concise`, `🛡️ De-escalate`, and `👔 Formal`.
- 🪄 **Zero-Latency Offline Transformations (No AI):** Instant deterministic text formatting (0 ms, zero GPU load):
  - **Case:** Make Upper Case, Make Lower Case, Capitalize Words, Sentence Case.
  - **Formatting:** Clean Extra Spaces (removes double spaces & collapses empty lines), Straighten Quotes (`«»“”` → `""`), Remove Line Breaks (unwraps paragraphs).
- 📊 **Word-Level Visual Diff:** Real-time LCS diff algorithm highlights modifications inline (green for additions, strikethrough red for removals).
- 📈 **Live Hardware Telemetry (Windows):** Real-time monitoring of RAM usage and compute load:
  - Dynamically shows **NPU:** load when running on laptop Ryzen AI.
  - Dynamically shows **GPU:** load when running on desktop LM Studio.
- 🕒 **Recent Checks History:** Keeps your last 15 checks locally in secure app storage with quick `Restore` and `Copy` buttons.
- 🧪 **Automated Verification:** Comprehensive automated test suite with 20 built-in verification tests.

---

## 📂 Repository Structure (Monorepo)

```text
LocalWritingTools/
├── macos/                     # Native macOS app (Swift 5.9, SwiftUI, AppKit)
│   ├── Sources/               # Modular Swift sources
│   ├── build.sh               # Standalone zero-dependency build script
│   ├── AppIcon.icns           # Multi-resolution macOS icon
│   └── Package.swift          # SPM package configuration
├── windows/                   # Native Windows app (C# .NET 8 WPF, OLED dark theme)
│   ├── Models/                # Data models (AppSettings, FlmModel, HistoryItem)
│   ├── Services/              # Backend services (API, Diff, Telemetry, Hotkey, SmartCopy)
│   ├── tests/                 # Automated test suite (20 passing tests)
│   ├── MainWindow.xaml/.cs    # Main UI window, streaming, provider controls
│   └── WritingTools.csproj    # .NET 8 project file
├── shared/                    # Standardized system prompts & assets
│   └── prompts/               # Verified prompts for QuickFix, DetailedReview, Summarize
├── docs/                      # Architecture manuals and porting specifications
│   ├── MACOS_BUILD_GUIDE.md   # Complete macOS architecture & build guide
│   └── WINDOWS_PORT_SPEC.md   # Windows PRD & master specification
├── .gitignore                 # Optimized for Swift, Xcode, .NET, C#, and Visual Studio
└── README.md
```

---

## 🚀 Getting Started

### 1. Prerequisites (Local LLM Server)

#### Option A: LM Studio (Desktop PC with GPU or Mac)
1. Install [LM Studio](https://lmstudio.ai/).
2. Download any capable instruction-tuned model (e.g. `Qwen 2.5 7B / 14B`, `DeepSeek R1 / Distill`, `Llama 3.1 8B`).
3. Navigate to the **Developer Tab** (`<->`) in LM Studio and click **Start Server** on port `1234` with CORS enabled.

#### Option B: FastFlowLM (Laptop with AMD Ryzen AI NPU)
- Windows app connects to local FastFlowLM on port `52625` (`flm serve <model> --port 52625`).

---

### 2. Running on Windows (Desktop PC or Laptop)

#### Method 1: Run directly from repository (requires .NET 8 SDK)
```powershell
# 1. Clone repository
git clone https://github.com/Vasylenk014/LocalWritingTools.git
cd LocalWritingTools\windows

# 2. Run application
dotnet run

# 3. (Optional) Run automated tests
dotnet run --project tests/WritingTools.Tests.csproj
```

#### Method 2: Build standalone portable EXE (No .NET installation required)
```powershell
cd LocalWritingTools\windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-standalone
```
Double-click `publish-standalone\WritingTools.exe` to run anywhere.

---

### 3. Running on macOS

```bash
# 1. Clone the repository
git clone https://github.com/Vasylenk014/LocalWritingTools.git
cd LocalWritingTools/macos

# 2. Build release bundle
chmod +x build.sh
./build.sh

# 3. Install into /Applications
cp -R WritingTools.app /Applications/
xattr -cr /Applications/WritingTools.app
open /Applications/WritingTools.app
```

The menu bar icon will appear in your top bar. Set up `Login Items` in macOS Settings if you want it to launch on boot.
For troubleshooting and detailed architecture, check [docs/MACOS_BUILD_GUIDE.md](docs/MACOS_BUILD_GUIDE.md).

---

## 🔒 Privacy, Security & Corporate Compliance

Local Writing Tools is engineered specifically for corporate and regulated environments where customer data, internal communications, and intellectual property must never leave the employee workstation.

- **Air-Gapped & Zero Cloud:** 100% of LLM inference runs locally on the workstation (NPU or GPU). Zero dependencies on external cloud APIs (OpenAI, Anthropic, AWS, etc.).
- **Zero External Telemetry:** No analytics, trackers, or crash reporting services are included.
- **Strict Network Boundary:** Network traffic is strictly isolated to internal loopback socket connections (`http://127.0.0.1:52625` for FastFlowLM, `http://localhost:1234` for LM Studio).
- **Stateless Requests:** Requests use `"store": false` to ensure context and ticket history are never cached in model memory or persistent server databases.
- **Zero Data Transmission (100% Local):** All drafts, prompts, and correspondence remain strictly on the local machine. The application never sends data over the internet, eliminating third-party data processor agreements (DPA) and data leak risks.
- **Auditable & Open:** Read our full [Security Architecture & Audit Guide (SECURITY.md)](SECURITY.md) for network verification steps using PowerShell and Wireshark.

---

## 📄 License

Distributed under the MIT License.
