# Security & Privacy Policy 🔒

This document outlines the security architecture, data handling practices, and compliance considerations for **Local Writing Tools**.

---

## 🏛️ Executive Summary

**Local Writing Tools** is an air-gapped, privacy-first desktop writing assistant for macOS and Windows. It assists customer support agents and business professionals in drafting, refining, and summarizing correspondence using **100% locally hosted Large Language Models (LLMs)**.

### Key Guarantees:
- **Zero Cloud AI Dependencies:** All inference is performed on the local device (NPU / GPU).
- **Zero External Telemetry:** No analytics, tracking, or crash-reporting services are embedded or contacted.
- **Air-Gapped Compatibility:** Functions completely without an internet connection once local model weights are present.
- **No Third-Party Data Processing:** Customer communications never leave the host operating system.

---

## 🛡️ Architecture & Data Flow

```
┌──────────────────────────────────────────────────────────────┐
│                    Host Machine (Local Only)                 │
│                                                              │
│  ┌───────────────────────┐         Local Loopback Socket     │
│  │ Local Writing Tools   │       (127.0.0.1 / localhost)     │
│  │ (Desktop App UI)      │ ─────────────────────────────────┐│
│  └───────────────────────┘                                  ││
│             ▲                                               ││
│             │ User-Triggered Hotkey (Alt+Shift+G / ⌥⌘G)     ││
│             ▼                                               ││
│  ┌───────────────────────┐                                  ││
│  │ Operating System      │                                  ││
│  │ Clipboard / Memory    │                                  ││
│  └───────────────────────┘                                  ││
│                                                             ▼│
│                                      ┌──────────────────────┐│
│                                      │ Local LLM Server     ││
│                                      │ • FastFlowLM (:52625)││
│                                      │ • LM Studio (:1234)  ││
│                                      │ • Ollama (:11434)    ││
│                                      └──────────────────────┘│
└──────────────────────────────────────────────────────────────┘
                                ✕
                      NO OUTBOUND INTERNET TRAFFIC
```

---

## 🔒 Security Specifications

### 1. Network Boundary
- **Loopback-Only Communication:** Network calls are strictly limited to `127.0.0.1` and `localhost`:
  - Port `52625`: FastFlowLM (AMD Ryzen AI NPU)
  - Port `1234`: LM Studio (NVIDIA / AMD GPU)
  - Port `11434`: Custom local OpenAI-compatible endpoints (e.g. Ollama)
- **Zero Outbound Calls:** The application makes **zero** outbound HTTP/HTTPS requests to the public internet during normal operation.
- **No Analytics / Telemetry:** The codebase contains no tracking SDKs (no Google Analytics, Sentry, Mixpanel, Datadog, etc.).

### 2. Ephemeral Data & Stateless Inference
- **Stateless Execution:** All requests sent to local inference servers include `"store": false` to prevent local model engines from caching conversations in persistent chat databases.
- **No Model Fine-Tuning / Retention:** Prompt inputs and draft texts are never stored, queued, or used for model retraining.

### 3. Data at Rest & Storage
- **Settings & Preferences:** Stored locally in the user's protected profile:
  - **Windows:** `%APPDATA%\WritingTools\settings.json`
  - **macOS:** App sandboxed `UserDefaults`
- **Recent History:** Retains up to 15 recent checks locally in `settings.json` exclusively for the user's convenience (`Restore` / `Copy`). This history can be cleared at any time via the UI.
- **Diagnostic Logging:** System logs (`%TEMP%\WritingTools_log.txt`) record only application lifecycle events (process start, exit code, mutex initialization). **Draft texts, customer names, emails, and prompt contents are never written to log files.**

### 4. Clipboard Security
- **Explicit Trigger Only:** The clipboard is read exclusively when the user explicitly triggers the global hotkey (`Alt+Shift+G` or `⌥⌘G`) or clicks "Paste".
- **No Background Polling:** The app does not run background clipboard sniffers, listeners, or clipboard history grabbers.

---

## 📋 Regulatory & Compliance Alignment

| Standard / Framework | Status | Compliance Details |
|---|---|---|
| **GDPR (EU 2016/679)** | **Compliant** | Zero personal data is transmitted across boundaries. No third-party data processor agreements (DPA) required. |
| **SOC 2 Type II** | **Aligned** | Zero cloud ingestion; eliminates risk of confidential customer ticket disclosure. |
| **HIPAA / HITECH** | **Aligned** | Suitable for environments handling sensitive health or personal data, as no protected health information (PHI) leaves the workstation. |
| **PCI-DSS** | **Aligned** | Cardholder data / payment information drafts are not transmitted over external networks. |

---

## 🔍 Verification Guide for IT & Security Auditors

Corporate security teams can easily audit and verify the application's local-only behavior:

### 1. Active Socket Audit
Run PowerShell to verify that WritingTools connects exclusively to loopback addresses:
```powershell
Get-NetTCPConnection -OwningProcess (Get-Process WritingTools).Id | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State
```
*Expected result:* All `RemoteAddress` entries are `127.0.0.1` or `::1`.

### 2. Network Sniffer Audit (Wireshark / Fiddler)
Filter for the `WritingTools.exe` process in Wireshark or Microsoft Network Monitor. When performing text improvement and summarization:
- Zero external DNS lookups.
- Zero outbound TCP/TLS connections to external IP addresses.

### 3. Binary Integrity & Open Source Inspection
The source code is fully open and auditable under the MIT License in this repository.

---

## 📬 Reporting Vulnerabilities

If you identify a security issue or potential vulnerability, please report it privately via GitHub Security Advisories or by contacting the project maintainer.
