import SwiftUI
import AppKit
import ComicCore

@main
struct ComicReaderApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var session = ReaderSession.shared
    var body: some Scene {
        Window("水漫", id: "main") {
            ContentView(session: session)
                .frame(minWidth: 880, minHeight: 600)
                .onOpenURL { session.open($0) }
                .onReceive(NotificationCenter.default.publisher(for: NSApplication.willTerminateNotification)) { _ in session.save() }
        }
        .defaultSize(width: 1180, height: 800)
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("打开漫画…") { session.chooseFile() }.keyboardShortcut("o")
                Button("返回书库") { session.home() }.keyboardShortcut("l", modifiers: [.command, .shift])
            }
            CommandMenu("阅读") {
                Button(session.readingControlsVisible ? "隐藏阅读控件" : "显示阅读控件") { session.toggleReadingControls() }.keyboardShortcut("h",modifiers:[.command,.shift]).disabled(!session.isReading)
                Button("切换全屏") {session.toggleFullScreen()}.keyboardShortcut("f",modifiers:[.command,.control])
                Divider()
                Button("向左翻页") { session.physicalArrow(left: true) }.keyboardShortcut(.leftArrow, modifiers: [])
                Button("向右翻页") { session.physicalArrow(left: false) }.keyboardShortcut(.rightArrow, modifiers: [])
                Divider()
                Button("顺时针旋转") { session.rotate(90) }.keyboardShortcut("r", modifiers: [])
                Button("逆时针旋转") { session.rotate(-90) }.keyboardShortcut("r", modifiers: [.shift])
                Button("添加或移除书签") { session.toggleBookmark() }.keyboardShortcut("d")
                Button("适合窗口") { session.zoom = 1; session.fitWidth = false }.keyboardShortcut("0")
                Button("放大") { session.zoom = min(6, session.zoom*1.25) }.keyboardShortcut("+")
                Button("缩小") { session.zoom = max(0.5, session.zoom/1.25) }.keyboardShortcut("-")
            }
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular); NSApp.activate(ignoringOtherApps: true)
        if let path = CommandLine.arguments.dropFirst().first(where: { !$0.hasPrefix("-") }), FileManager.default.fileExists(atPath: path) { ReaderSession.shared.open(URL(fileURLWithPath: path)) }
    }
    func application(_ sender: NSApplication, openFiles filenames: [String]) {
        if let file = filenames.first { ReaderSession.shared.open(URL(fileURLWithPath: file)) }
        sender.reply(toOpenOrPrint: .success)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
