import SwiftUI
import AppKit
import Carbon

@main
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var popover: NSPopover!
    private var viewModel: ViewModel!
    private var hotKey: HotKey?
    private var globalEventMonitor: Any?

    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupMainMenu()

        let viewModel = ViewModel()
        self.viewModel = viewModel

        let hosting = NSHostingController(rootView: ContentView(vm: viewModel))
        hosting.sizingOptions = [.preferredContentSize]

        let popover = NSPopover()
        popover.behavior = .transient
        popover.contentViewController = hosting
        self.popover = popover
        
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = self.statusItem.button {
            let img = NSImage(systemSymbolName: "text.badge.checkmark", accessibilityDescription: "Writing Tools")
            img?.isTemplate = true
            button.image = img
            button.action = #selector(togglePopover(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }

        // ⌥⌘G: improve whatever is in the clipboard, from any app.
        hotKey = HotKey(keyCode: UInt32(kVK_ANSI_G), modifiers: UInt32(cmdKey | optionKey)) { [weak self] in
            self?.showPopover()
            self?.viewModel.improveClipboard()
        }
        
        setupEventMonitors()
    }

    /// Enables standard macOS keyboard shortcuts (⌘C, ⌘V, ⌘A, ⌘X, ⌘Z) in Menu Bar accessory apps.
    private func setupMainMenu() {
        let mainMenu = NSMenu()
        let editMenuItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")

        editMenu.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        let redoItem = NSMenuItem(title: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        editMenu.addItem(redoItem)
        editMenu.addItem(NSMenuItem.separator())
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")

        editMenu.addItem(NSMenuItem.separator())
        let transformMenuItem = NSMenuItem(title: "Transformations", action: nil, keyEquivalent: "")
        let transformMenu = NSMenu(title: "Transformations")
        transformMenu.addItem(withTitle: "Make Upper Case", action: Selector(("uppercaseWord:")), keyEquivalent: "")
        transformMenu.addItem(withTitle: "Make Lower Case", action: Selector(("lowercaseWord:")), keyEquivalent: "")
        transformMenu.addItem(withTitle: "Capitalize", action: Selector(("capitalizeWord:")), keyEquivalent: "")
        transformMenuItem.submenu = transformMenu
        editMenu.addItem(transformMenuItem)

        editMenuItem.submenu = editMenu
        mainMenu.addItem(editMenuItem)
        NSApp.mainMenu = mainMenu
    }
    
    private func setupEventMonitors() {
        // Safe dispatch to main queue so background events never crash the process
        globalEventMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            DispatchQueue.main.async {
                if let popover = self?.popover, popover.isShown {
                    self?.closePopover()
                }
            }
        }
    }
    
    @objc func togglePopover(_ sender: AnyObject?) {
        if popover.isShown {
            closePopover()
        } else {
            showPopover()
        }
    }
    
    private func showPopover() {
        guard let button = statusItem.button else { return }
        if !popover.isShown {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        }
        NSApp.activate(ignoringOtherApps: true)
        popover.contentViewController?.view.window?.makeKey()
    }
    
    private func closePopover() {
        popover.performClose(nil)
    }
}
