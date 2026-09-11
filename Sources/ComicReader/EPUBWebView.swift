import SwiftUI
import WebKit
import UniformTypeIdentifiers
import ComicCore

struct EPUBWebView: NSViewRepresentable {
    let url: URL
    let resource: String
    let revision: String
    func makeCoordinator() -> Coordinator { Coordinator() }
    func makeNSView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = false
        let handler = LocalBookHandler(url: url)
        configuration.setURLSchemeHandler(handler, forURLScheme: "comic-book")
        let web = WKWebView(frame: .zero, configuration: configuration)
        web.navigationDelegate = context.coordinator
        context.coordinator.handler = handler
        return web
    }
    func updateNSView(_ view: WKWebView, context: Context) {
        let key = "\(revision):\(resource)"
        guard key != context.coordinator.loaded else { return }
        context.coordinator.loaded = key
        var components = URLComponents(); components.scheme = "comic-book"; components.host = "publication"; components.path = "/"+resource
        if let target = components.url { view.load(URLRequest(url: target)) }
    }
    final class Coordinator: NSObject, WKNavigationDelegate {
        var loaded = ""
        var handler: LocalBookHandler?
        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
            if url.scheme == "comic-book", url.host == "publication" { decisionHandler(.allow) }
            else {
                if navigationAction.navigationType == .linkActivated, ["https", "http"].contains(url.scheme ?? "") { NSWorkspace.shared.open(url) }
                decisionHandler(.cancel)
            }
        }
    }
}

final class LocalBookHandler: NSObject, WKURLSchemeHandler {
    private let archive: BookArchive?
    private let queue = DispatchQueue(label: "ComicReader.WebResources", qos: .userInitiated)
    private let lock = NSLock()
    private var canceled: Set<ObjectIdentifier> = []
    init(url: URL) { archive = try? BookArchive(url: url); super.init() }
    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        let id = ObjectIdentifier(task)
        lock.lock(); canceled.remove(id); lock.unlock()
        queue.async { [self] in
            do {
                guard let url = task.request.url, url.host == "publication", let archive else { throw ReaderError.message("本地资源不可用。") }
                let path = try BookArchive.normalize(String(url.path.dropFirst()))
                var data = try archive.data(path)
                let ext = (path as NSString).pathExtension.lowercased()
                let html = ["html", "xhtml", "htm"].contains(ext)
                let mime = html ? "text/html" : (UTType(filenameExtension: ext)?.preferredMIMEType ?? "application/octet-stream")
                if html {
                    // CSP is placed before book markup so remote subresources and scripts are blocked.
                    let policy = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src comic-book: data:; style-src comic-book: 'unsafe-inline'; font-src comic-book: data:; script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'\">"
                    data = Data((policy + String(decoding: data, as: UTF8.self)).utf8)
                }
                DispatchQueue.main.async { [self] in
                    lock.lock(); let stopped = canceled.contains(id); lock.unlock()
                    guard !stopped else { return }
                    task.didReceive(URLResponse(url: url, mimeType: mime, expectedContentLength: data.count, textEncodingName: html ? "utf-8" : nil))
                    task.didReceive(data); task.didFinish()
                }
            } catch {
                DispatchQueue.main.async { [self] in
                    lock.lock(); let stopped = canceled.contains(id); lock.unlock()
                    if !stopped { task.didFailWithError(error) }
                }
            }
        }
    }
    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {
        lock.lock(); canceled.insert(ObjectIdentifier(task)); lock.unlock()
    }
}
