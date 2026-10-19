import SwiftUI

struct ContentView: View {
    @ObservedObject var vm: ViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            header
            if vm.showHistory {
                historyView
            } else {
                mainContent
            }
            footer
        }
        .padding(14)
        .frame(width: 470)
        .onAppear { vm.fetchModels() }
    }

    // MARK: - Main Sections

    private var header: some View {
        HStack {
            Text("Local Writing Tools").font(.headline)
            Spacer()
            
            Button {
                withAnimation(.easeInOut(duration: 0.2)) {
                    vm.showHistory.toggle()
                }
            } label: {
                HStack(spacing: 3) {
                    Image(systemName: vm.showHistory ? "text.bubble" : "clock.arrow.circlepath")
                    if !vm.history.isEmpty && !vm.showHistory {
                        Text("\(vm.history.count)")
                            .font(.system(size: 10, weight: .bold))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 1)
                            .background(Color.accentColor.opacity(0.2))
                            .clipShape(Capsule())
                    }
                }
            }
            .buttonStyle(.plain)
            .help(vm.showHistory ? "Back to Editor" : "View Recent History")

            Text("⌥⌘G")
                .font(.caption2)
                .padding(.horizontal, 5)
                .padding(.vertical, 2)
                .background(Color.secondary.opacity(0.15))
                .clipShape(RoundedRectangle(cornerRadius: 4))
                .foregroundStyle(.secondary)
                .help("Hotkey: copy text anywhere (⌘C), press ⌥⌘G, then paste result (⌘V).")
        }
    }

    private var mainContent: some View {
        VStack(alignment: .leading, spacing: 10) {
            modelRow
            thinkingRow
            modeRow
            if vm.mode == .quick {
                toneRow
            }
            promptSection
            if vm.mode != .summarize {
                contextSection
            }
            inputSection
            actionRow
            resultSection
        }
    }

    private var modelRow: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(vm.isServerOnline ? Color.green : Color.red)
                .frame(width: 8, height: 8)
                .help(vm.isServerOnline ? "LM Studio is online" : "LM Studio is unreachable")

            Text("Model:")
                .font(.callout)

            if vm.availableModels.isEmpty {
                Text("No models found").foregroundStyle(.red).font(.caption)
            } else {
                Picker("", selection: $vm.selectedModel) {
                    ForEach(vm.availableModels) { model in
                        Text(model.isLoaded ? "● \(model.displayName)" : model.displayName).tag(model.key)
                    }
                }
                .labelsHidden()
                .help("● = loaded in memory")
            }
            Spacer()
            if vm.hasLoadedModels {
                Button {
                    vm.unloadAllModels()
                } label: {
                    Image(systemName: "eject")
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Unload models from GPU VRAM")
                .disabled(vm.isUnloading)
            }
            Button { vm.fetchModels() } label: { Image(systemName: "arrow.clockwise") }
                .buttonStyle(.plain)
                .help("Refresh models")
        }
    }

    @ViewBuilder
    private var thinkingRow: some View {
        if let model = vm.currentModel {
            HStack {
                Text("Thinking:")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if model.reasoningOptions.isEmpty {
                    Text("not configurable for this model")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Spacer()
                } else {
                    Picker("", selection: $vm.reasoningChoice) {
                        Text("Auto").tag(ViewModel.autoReasoning)
                        ForEach(model.reasoningOptions, id: \.self) { option in
                            Text(option.capitalized).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .controlSize(.small)
                    .help("Auto = model default. Off = fastest.")
                }
            }
        }
    }

    private var modeRow: some View {
        Picker("", selection: $vm.mode) {
            ForEach(Mode.allCases) { Text($0.title).tag($0) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
    }

    private var toneRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(Tone.allCases) { tone in
                    Button {
                        vm.selectedTone = tone
                    } label: {
                        Text(tone.title)
                            .font(.system(size: 11, weight: vm.selectedTone == tone ? .semibold : .regular))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .background(vm.selectedTone == tone ? Color.accentColor.opacity(0.18) : Color.secondary.opacity(0.1))
                            .foregroundStyle(vm.selectedTone == tone ? Color.accentColor : Color.primary)
                            .clipShape(Capsule())
                            .overlay(
                                Capsule()
                                    .stroke(vm.selectedTone == tone ? Color.accentColor.opacity(0.6) : Color.clear, lineWidth: 1)
                            )
                    }
                    .buttonStyle(.plain)
                    .help(tone.instruction)
                }
            }
            .padding(.vertical, 2)
        }
    }

    private var promptSection: some View {
        DisclosureGroup(isExpanded: $vm.showPrompt) {
            VStack(alignment: .trailing, spacing: 6) {
                editor($vm.currentPrompt, height: 180, fontSize: 11)
                Button("Reset to default") { vm.resetPrompt() }
                    .controlSize(.small)
            }
            .padding(.top, 4)
        } label: {
            Text("System prompt — \(vm.mode.title)").font(.caption).foregroundStyle(.secondary)
        }
    }

    private var contextSection: some View {
        DisclosureGroup(isExpanded: $vm.showContext) {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Customer message or thread history")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button("Paste") { vm.pasteContext() }.controlSize(.mini)
                    if !vm.contextText.isEmpty {
                        Button("Clear") { vm.clearContext() }.controlSize(.mini)
                    }
                }
                editor($vm.contextText, height: 75, fontSize: 12)
            }
            .padding(.top, 4)
        } label: {
            HStack(spacing: 6) {
                Text("Ticket context / Client message")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if !vm.contextText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("Active")
                        .font(.system(size: 9, weight: .bold))
                        .padding(.horizontal, 5)
                        .padding(.vertical, 1)
                        .background(Color.accentColor.opacity(0.18))
                        .foregroundStyle(Color.accentColor)
                        .clipShape(Capsule())
                } else {
                    Text("(optional)")
                        .font(.caption2)
                        .foregroundStyle(.secondary.opacity(0.7))
                }
            }
        }
    }

    private var inputSection: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(vm.mode == .summarize ? "Customer's message" : "Draft")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Paste") { vm.pasteFromClipboard() }.controlSize(.small)

                Menu {
                    Section("Case") {
                        Button {
                            vm.applyTransformation(.uppercase)
                        } label: {
                            Label("Make Upper Case", systemImage: "textformat.size.larger")
                        }
                        Button {
                            vm.applyTransformation(.lowercase)
                        } label: {
                            Label("Make Lower Case", systemImage: "textformat.size.smaller")
                        }
                        Button {
                            vm.applyTransformation(.capitalize)
                        } label: {
                            Label("Capitalize", systemImage: "textformat")
                        }
                        Button {
                            vm.applyTransformation(.sentenceCase)
                        } label: {
                            Label("Sentence case", systemImage: "text.alignleft")
                        }
                    }

                    Section("Formatting") {
                        Button {
                            vm.applyTransformation(.cleanSpacing)
                        } label: {
                            Label("Clean Extra Spaces", systemImage: "arrow.left.and.right.text.vertical")
                        }
                        Button {
                            vm.applyTransformation(.straightenQuotes)
                        } label: {
                            Label("Straighten Quotes (\" ')", systemImage: "quote.opening")
                        }
                        Button {
                            vm.applyTransformation(.removeLineBreaks)
                        } label: {
                            Label("Remove Line Breaks", systemImage: "text.word.spacing")
                        }
                    }
                } label: {
                    HStack(spacing: 3) {
                        Image(systemName: "textformat")
                        Text("Transform")
                    }
                }
                .menuStyle(.borderedButton)
                .controlSize(.small)
                .disabled(vm.inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)

                Button("Clear") { vm.clear() }.controlSize(.small)
            }
            editor($vm.inputText, height: vm.mode == .summarize ? 120 : 95, fontSize: 13)
        }
    }

    private var actionRow: some View {
        HStack {
            Toggle("Auto-copy", isOn: $vm.autoCopy)
                .toggleStyle(.checkbox)
                .font(.caption)
            Spacer()
            if vm.isProcessing {
                Button("Stop") { vm.stop() }
                    .controlSize(.small)
            } else {
                Button(actionButtonTitle) { vm.run() }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.return, modifiers: .command)
                    .disabled(!vm.canRun)
            }
        }
    }

    private var actionButtonTitle: String {
        vm.mode == .summarize ? "Summarize  ⌘↩" : "Improve  ⌘↩"
    }

    private var resultSection: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(vm.resultMode == .summarize ? "Summary" : "Result")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                if let status = vm.statusText {
                    Text(status).font(.caption2).foregroundStyle(.secondary)
                }
                if vm.resultMode == .quick && !vm.outputText.isEmpty {
                    Picker("", selection: $vm.showDiff) {
                        Text("Text").tag(false)
                        Text("Diff").tag(true)
                    }
                    .pickerStyle(.segmented)
                    .controlSize(.mini)
                    .frame(width: 90)
                }
            }

            Group {
                if vm.resultMode == .detailed && !vm.outputText.isEmpty {
                    detailedResult
                } else if vm.resultMode == .summarize && !vm.outputText.isEmpty {
                    summarizeResult
                } else if vm.showDiff && !vm.outputText.isEmpty {
                    diffResult
                } else {
                    editor($vm.outputText, height: 120, fontSize: 13)
                }
            }
            .overlay {
                if vm.isProcessing && vm.outputText.isEmpty { ProgressView() }
            }

            if let error = vm.errorText {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var diffResult: some View {
        ScrollView {
            Text(vm.diffAttributedString)
                .font(.system(size: 13))
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
        }
        .frame(height: 120)
        .background(Color(nsColor: .textBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.25)))
    }

    private var detailedResult: some View {
        ScrollView {
            Text(markdown(vm.outputText))
                .font(.system(size: 13))
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
        }
        .frame(height: 220)
        .background(Color(nsColor: .textBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.25)))
    }

    private var summarizeResult: some View {
        ScrollView {
            Text(markdown(vm.outputText))
                .font(.system(size: 13))
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
        }
        .frame(height: 150)
        .background(Color(nsColor: .textBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.25)))
    }

    // MARK: - History View

    private var historyView: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Recent Checks").font(.subheadline).bold()
                Spacer()
                if !vm.history.isEmpty {
                    Button("Clear History") { vm.clearHistory() }
                        .controlSize(.small)
                }
            }

            if vm.history.isEmpty {
                VStack(spacing: 8) {
                    Image(systemName: "tray")
                        .font(.largeTitle)
                        .foregroundStyle(.secondary)
                    Text("No recent history yet")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, minHeight: 200)
            } else {
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(vm.history) { item in
                            historyCard(item)
                        }
                    }
                    .padding(.trailing, 4)
                }
                .frame(height: 380)
            }
        }
    }

    private func historyCard(_ item: HistoryItem) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(item.timestamp, style: .time)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                
                Text(item.mode)
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 4)
                    .padding(.vertical, 1)
                    .background(Color.secondary.opacity(0.15))
                    .clipShape(Capsule())

                if let tone = item.tone {
                    Text(tone)
                        .font(.system(size: 9, weight: .medium))
                        .padding(.horizontal, 4)
                        .padding(.vertical, 1)
                        .background(Color.accentColor.opacity(0.15))
                        .clipShape(Capsule())
                }
                Spacer()
                Button("Restore") {
                    vm.restoreHistory(item)
                }
                .controlSize(.mini)
                
                Button("Copy") {
                    let pb = NSPasteboard.general
                    pb.clearContents()
                    pb.setString(item.output, forType: .string)
                }
                .controlSize(.mini)
            }

            Text(item.output)
                .font(.system(size: 12))
                .lineLimit(3)
                .foregroundStyle(.primary)

            if let ctx = item.context, !ctx.isEmpty {
                Text("Context: \(ctx)")
                    .font(.system(size: 10))
                    .lineLimit(1)
                    .foregroundStyle(.secondary)
            }

            Text("Draft: \(item.input)")
                .font(.system(size: 10))
                .lineLimit(1)
                .foregroundStyle(.secondary)
        }
        .padding(8)
        .background(Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.15)))
    }

    private var footer: some View {
        HStack {
            Button(copyTitle) { vm.copyResult() }
                .keyboardShortcut("c", modifiers: [.command, .shift])
                .disabled(vm.outputText.isEmpty || vm.showHistory)
            Spacer()
            Button("Quit") { NSApp.terminate(nil) }
        }
    }

    private var copyTitle: String {
        if vm.copied { return "Copied ✓" }
        switch vm.resultMode {
        case .detailed: return "Copy corrected  ⇧⌘C"
        case .summarize: return "Copy summary  ⇧⌘C"
        case .quick: return "Copy Result  ⇧⌘C"
        }
    }

    // MARK: - Helpers

    private func markdown(_ text: String) -> AttributedString {
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: text, options: options)) ?? AttributedString(text)
    }

    private func editor(_ text: Binding<String>, height: CGFloat, fontSize: CGFloat) -> some View {
        TextEditor(text: text)
            .font(.system(size: fontSize))
            .scrollContentBackground(.hidden)
            .padding(4)
            .background(Color(nsColor: .textBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 6))
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.25)))
            .frame(height: height)
    }
}
