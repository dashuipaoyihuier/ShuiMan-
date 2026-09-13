import SwiftUI
import AppKit

/// Observe the existing SwiftUI window without replacing its delegate.
struct ReadingWindowBridge: NSViewRepresentable {
    @ObservedObject var session: ReaderSession
    func makeNSView(context: Context) -> ReadingWindowView {
        let view=ReadingWindowView();view.session=session;return view
    }
    func updateNSView(_ view: ReadingWindowView, context: Context) { view.session=session;view.updateChrome() }
}

final class ReadingWindowView: NSView {
    weak var session: ReaderSession?
    private var observers:[NSObjectProtocol]=[]
    private var keyMonitor:Any?
    private var previousControls=false
    private var previousPresentation:NSApplication.PresentationOptions?
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        observers.forEach(NotificationCenter.default.removeObserver);observers=[]
        if let keyMonitor {NSEvent.removeMonitor(keyMonitor)};keyMonitor=nil
        guard let window else {return}
        session?.readingWindow=window
        window.styleMask.insert(.fullSizeContentView)
        for name in [NSWindow.didEnterFullScreenNotification,NSWindow.didExitFullScreenNotification] {
            observers.append(NotificationCenter.default.addObserver(forName:name,object:window,queue:.main) { [weak self] notification in
                guard let self else {return}
                if notification.name == NSWindow.didEnterFullScreenNotification {
                    self.previousControls=self.session?.readingControlsVisible ?? false
                    self.session?.readingControlsVisible=false
                    self.previousPresentation=NSApp.presentationOptions
                    var options=NSApp.presentationOptions
                    options.remove([.hideMenuBar,.hideDock]);options.formUnion([.autoHideMenuBar,.autoHideDock])
                    NSApp.presentationOptions=options
                } else {
                    if let original=self.previousPresentation {
                        // Fullscreen itself belongs to AppKit; restore only the options we changed.
                        var options=NSApp.presentationOptions
                        options.subtract([.autoHideMenuBar,.autoHideDock,.hideMenuBar,.hideDock])
                        options.formUnion(original.intersection([.autoHideMenuBar,.autoHideDock,.hideMenuBar,.hideDock]))
                        NSApp.presentationOptions=options
                    }
                    self.previousPresentation=nil
                    self.session?.readingControlsVisible=self.previousControls
                }
                self.updateChrome()
            })
        }
        keyMonitor=NSEvent.addLocalMonitorForEvents(matching:.keyDown) { [weak self] event in
            guard let self,let window=self.window,event.window===window,window.attachedSheet==nil,
                  self.session?.isReading==true,!(window.firstResponder is NSTextView),
                  event.modifierFlags.intersection([.command,.control,.option,.shift]).isEmpty else {return event}
            if event.keyCode==126,self.session?.showWeb==false {
                // One quarter turn per press; holding the key must not spin the page.
                if !event.isARepeat { self.session?.rotate(90) }
                return nil
            }
            if event.keyCode==48 {
                self.session?.toggleReadingControls();return nil
            }
            if event.keyCode==53,!window.styleMask.contains(.fullScreen),self.session?.readingControlsVisible==false {
                self.session?.toggleReadingControls();return nil
            }
            return event
        }
        updateChrome()
    }
    func updateChrome() {
        guard let window else {return}
        let hidden=session?.isReading==true && session?.readingControlsVisible==false
        for type in [NSWindow.ButtonType.closeButton,.miniaturizeButton,.zoomButton] {window.standardWindowButton(type)?.isHidden=hidden}
    }
    deinit {
        observers.forEach(NotificationCenter.default.removeObserver)
        if let keyMonitor {NSEvent.removeMonitor(keyMonitor)}
    }
}
