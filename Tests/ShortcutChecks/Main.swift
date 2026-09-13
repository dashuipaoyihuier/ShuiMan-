import AppKit
import SwiftUI
@MainActor final class ReaderSession: ObservableObject {
    var readingWindow: NSWindow?
    var readingControlsVisible = true
    var isReading = true
    var showWeb = false
    var angle = 0
    var turns = 0
    func toggleReadingControls() { readingControlsVisible.toggle() }
    func rotate(_ value: Int) { angle = (angle + value) % 360; turns += 1 }
}
@main struct ShortcutChecks {
    @MainActor static func main() {
        let app = NSApplication.shared
        let session = ReaderSession()
        let window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 900,height: 600),styleMask: [.titled],backing: .buffered,defer: false)
        let bridge = ReadingWindowView(); bridge.session = session
        window.contentView = bridge
        func key(_ code: UInt16 = 126, flags: NSEvent.ModifierFlags = [], repeatKey: Bool = false) {
            let event = NSEvent.keyEvent(with: .keyDown,location: .zero,modifierFlags: flags,timestamp: 0,windowNumber: window.windowNumber,context: nil,characters: "",charactersIgnoringModifiers: "",isARepeat: repeatKey,keyCode: code)!
            app.postEvent(event,atStart: true)
            if let queued = app.nextEvent(matching: .keyDown,until: Date().addingTimeInterval(1),inMode: .default,dequeue: true) { app.sendEvent(queued) }
        }
        func check(_ good: Bool,_ text: String) { if !good { fatalError(text) }; print("PASS: \(text)") }
        key(); check(session.angle == 90,"Up rotates once")
        key(repeatKey: true); check(session.turns == 1,"Held Up does not repeat")
        key(); key(); key(); check(session.angle == 0 && session.turns == 4,"Four presses restore orientation")
        for flags: NSEvent.ModifierFlags in [.shift,.command,.control,.option] { key(flags: flags) }
        check(session.turns == 4,"Modified Up is preserved")
        let input = NSTextView(frame: NSRect(x: 0,y: 0,width: 100,height: 30)); bridge.addSubview(input); window.makeFirstResponder(input)
        key(); check(session.turns == 4,"Text editing does not rotate")
        window.makeFirstResponder(nil)
        session.isReading = false; key(); check(session.turns == 4,"Library does not rotate")
        session.isReading = true; session.showWeb = true; key(); check(session.turns == 4,"Original EPUB layout does not rotate")
        session.showWeb = false; session.readingControlsVisible = false; key(); check(session.angle == 90,"Hidden reading controls still allow rotation")
        print("8 native AppKit shortcut checks passed")
    }
}
