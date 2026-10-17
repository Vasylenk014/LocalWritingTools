# Local Writing Tools ✍️

[![Platform: macOS | Windows](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows-brightgreen.svg)](#-repository-structure-monorepo)
[![Privacy: 100% Local](https://img.shields.io/badge/Privacy-100%25%20Local-success.svg)](#-privacy--local-security)
[![Engine: LM Studio v1](https://img.shields.io/badge/Engine-LM%20Studio%20v1%20REST-orange.svg)](#-getting-started)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](#-license)

> **Local Writing Tools** is a private, lightweight menu bar and system tray writing companion designed specifically for customer support specialists (e.g. Zendesk). It connects to locally hosted LLMs via **LM Studio** to refine, edit, and summarize support drafts instantly with **zero cloud dependencies**, zero subscription fees, and no risk of customer data leaks.

---

## 🔒 Why Local Writing Tools?

Customer support teams handle sensitive customer information, ticket threads, order numbers, and personal details daily. Pasting customer communications into cloud-based AI tools (OpenAI, Anthropic, etc.) creates compliance risks and compromises customer privacy.

**Local Writing Tools** solves this by:
- Running **100% on your local machine** using local models via LM Studio.
- Using **stateless REST requests** (`"store": false`) so ticket context is never retained or indexed into model memory.
- Operating unobtrusively in your **macOS Menu Bar** or **Windows System Tray** with quick global hotkeys.

---

## ✨ Features

- ⚡️ **Global Hotkey Workflow:** Select any text anywhere (Zendesk, email, browser), press `⌥⌘G` (macOS) or `Alt+Shift+G` (Windows), and the app automatically grabs your clipboard, generates an improved response, and copies it back to your clipboard (`Auto-copy`).
- 🛠️ **3 Purpose-Built Support Modes:**
  - **Quick Fix:** Grammatical correction, clarity, and tone improvements while strictly preserving original meaning, length, facts, and Zendesk placeholders (`{{ticket.requester.first_name}}`).
  - **Detailed Review:** Structured feedback broken down into: `CORRECTED`, `CONCISE`, `CHANGES` (with grammar/style rationale), and `NOTES`.
  - **Summarize:** Extracts core issues, key details, and the customer's goal from lengthy, AI-generated customer messages in 5 seconds.
- 💬 **Ticket Context / Thread History:** An optional collapsible context section (`Ticket context`). Pass previous messages or the client's inquiry inside `<context>...</context>` so the model tailors its tone and answers specific questions while rewriting **only** your draft.
- 🎭 **Tone Presets (Quick Fix):** Instant tone modifiers: `✨ Standard`, `🤝 Friendly`, `⚡️ Concise`, `🛡️ De-escalate`, and `👔 Formal`.
- 🪄 **Zero-Latency Offline Transformations (No AI):** Instant deterministic text formatting (0 ms, zero GPU load):
  - **Case:** Make Upper Case, Make Lower Case, Capitalize Words, Sentence Case.
  - **Formatting:** Clean Extra Spaces (removes double spaces & collapses empty lines), Straighten Quotes (`«»“”` → `""`), Remove Line Breaks (unwraps paragraphs).
  - Also accessible via native context menu (right-click on selection).
- 📊 **Word-Level Visual Diff:** Real-time LCS diff algorithm highlights modifications inline (green for additions, strikethrough red for removals).
- 🧠 **Granular Reasoning Controls:** Full control over thinking models (e.g. DeepSeek-R1, Qwen Reasoning): toggle reasoning between `Auto`, `Off`, `Low`, `Medium`, or `High`.
- 🕒 **Recent Checks History:** Keeps your last 15 checks locally in secure app storage with quick `Restore` and `Copy` buttons.

---

## 📂 Repository Structure (Monorepo)

```text
LocalWritingTools/
├── macos/                     # Native macOS app (Swift 5.9, SwiftUI, AppKit)
│   ├── Sources/               # Modular Swift sources
│   ├── build.sh               # Standalone zero-dependency build script
│   ├── AppIcon.icns           # Multi-resolution macOS icon
│   └── Package.swift          # SPM package configuration
├── windows/                   # Windows companion app
│   └── README.md              # Windows instructions and implementation files
├── docs/                      # Architecture manuals and porting specifications
│   ├── MACOS_BUILD_GUIDE.md   # Complete macOS architecture & build guide
│   └── WINDOWS_PORT_SPEC.md   # Windows PRD & master specification
├── .gitignore                 # Optimized for Swift, Xcode, .NET, C#, and Python
└── README.md
```

---

## 🚀 Getting Started

### 1. Prerequisites (LM Studio)
1. Install [LM Studio](https://lmstudio.ai/) (macOS / Windows).
2. Download any capable instruction-tuned model. Recommended models:
   - `Qwen 2.5 7B / 14B Instruct` (Outstanding multilingual performance)
   - `Gemma 2 9B` / `Gemma 3`
   - `Llama 3.1 8B` or `Llama 3.2`
   - `Mistral Nemo 12B`
3. Navigate to the **Developer Tab** (`<>`) in LM Studio and click **Start Server** on port `1234` with CORS enabled.

---

### 2. macOS Installation

You can build and package the native macOS app in seconds using the command line:

```bash
# Clone the repository
git clone https://github.com/Vasylenk014/LocalWritingTools.git
cd LocalWritingTools/macos

# Build release bundle
chmod +x build.sh
./build.sh

# Install into /Applications
cp -R WritingTools.app /Applications/
xattr -cr /Applications/WritingTools.app
open /Applications/WritingTools.app
```

The menu bar icon will appear in your top bar. Set up `Login Items` in macOS Settings if you want it to launch on boot.
For troubleshooting and detailed architecture, check [docs/MACOS_BUILD_GUIDE.md](docs/MACOS_BUILD_GUIDE.md).

---

### 3. Windows Installation

Refer to [windows/README.md](windows/README.md) and the comprehensive technical design in [docs/WINDOWS_PORT_SPEC.md](docs/WINDOWS_PORT_SPEC.md).

---

## 🔒 Privacy & Local Security

- **No telemetry or tracking.**
- **No external network calls.** The application connects strictly to your local host at `http://127.0.0.1:1234`.
- **Stateless execution.** Requests use `"store": false` to prevent history retention in LM Studio.
- **Local persistence.** Recent checks and custom prompts remain strictly in your local operating system preferences (`UserDefaults` on macOS, local app storage on Windows).

---

## 📄 License

Distributed under the MIT License.
