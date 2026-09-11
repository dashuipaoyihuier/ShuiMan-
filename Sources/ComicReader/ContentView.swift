import SwiftUI
import AppKit
import UniformTypeIdentifiers
import ComicCore

struct ContentView: View {
    @ObservedObject var session: ReaderSession
    @State private var dragging = false
    @State private var password = ""
    @State private var hoveringControls = false
    private var immersive: Bool { session.isReading && !session.readingControlsVisible }
    var body: some View {
        VStack(spacing: 0) {
            if !immersive { header; Divider() }
            if session.tab == .reading {
                if let publication = session.publication { reader(publication) }
                else { library }
            } else { LibraryView(model:session.libraryModel,session:session) }
            if !session.isReading { Divider(); bottomNavigation }
        }
        .background(immersive ? Color(white:0.075) : WaterTheme.surface)
        .ignoresSafeArea(.container,edges:immersive ? .all : [])
        .background(ReadingWindowBridge(session:session).frame(width:0,height:0))
        .overlay(alignment:.topTrailing) {
            if immersive {
                HStack(spacing:12) {
                    Button {session.toggleReadingControls()} label:{Label("显示阅读控件 · Tab",systemImage:"slider.horizontal.3")}
                    Button {session.toggleFullScreen()} label:{Image(systemName:"arrow.up.left.and.arrow.down.right")}.help("切换全屏 · ⌃⌘F")
                }.buttonStyle(.borderless).padding(14)
                    .background(.regularMaterial,in:RoundedRectangle(cornerRadius:10))
                    .opacity(hoveringControls ? 1 : 0)
                    .padding(8).contentShape(Rectangle()).onHover{hoveringControls=$0}
            }
        }
        .onChange(of:immersive) { _,_ in hoveringControls=false }
        .tint(WaterTheme.accent)
        .overlay {
            if session.busy {
                ZStack {
                    Color.black.opacity(0.18)
                    VStack(spacing: 14) {
                        ProgressView()
                        Text(session.status).font(.callout).lineLimit(2)
                    }.padding(32).frame(maxWidth: 420).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18))
                }
            }
            if dragging {
                RoundedRectangle(cornerRadius: 12).stroke(WaterTheme.accent, style: StrokeStyle(lineWidth: 3, dash: [8])).padding(12).allowsHitTesting(false)
            }
        }
        .onDrop(of: [UTType.fileURL.identifier], isTargeted: $dragging) { providers in
            guard let provider = providers.first else { return false }
            _ = provider.loadObject(ofClass: URL.self) { url, _ in
                if let url { Task { @MainActor in session.open(url) } }
            }; return true
        }
        .alert("无法完成操作", isPresented: Binding(get: { session.errorMessage != nil }, set: { if !$0 { session.errorMessage = nil } })) {
            Button("好", role: .cancel) { session.errorMessage = nil }
        } message: { Text(session.errorMessage ?? "") }
        .sheet(isPresented: Binding(get: { session.passwordURL != nil }, set: { if !$0 { session.passwordURL = nil } })) {
            VStack(alignment: .leading, spacing: 18) {
                Label("打开受保护的 PDF", systemImage: "lock.doc").font(.title2)
                SecureField("输入文档密码", text: $password).textFieldStyle(.roundedBorder).onSubmit { unlock() }
                HStack {
                    Spacer()
                    Button("取消") { session.passwordURL = nil; password = "" }
                    Button("打开") { unlock() }.buttonStyle(.borderedProminent)
                }
            }.padding(28).frame(width: 360)
        }
    }
    private var bottomNavigation: some View {
        HStack(spacing:0) {
            ForEach(LibraryTab.allCases,id:\.self) { tab in
                Button { session.save();session.tab=tab } label: {
                    VStack(spacing:5) {
                        Image(systemName:tab.icon).font(.system(size:25,weight:.regular))
                        Text(tab.rawValue).font(.system(size:12))
                    }.foregroundStyle(session.tab==tab ? WaterTheme.accent : Color.secondary)
                        .frame(maxWidth:.infinity).frame(height:68).contentShape(Rectangle())
                }.buttonStyle(.plain).accessibilityLabel(tab.rawValue)
            }
        }.background(WaterTheme.surface)
    }
    private func unlock() {
        guard let url = session.passwordURL else { return }
        let entered = password; password = ""; session.passwordURL = nil; session.open(url, password: entered)
    }
    private var header: some View {
        HStack(spacing: 16) {
            if session.publication != nil && session.tab == .reading {
                Button { session.home() } label: { Image(systemName: "books.vertical") }.help("返回书库")
                Button { session.showSidebar.toggle() } label: { Image(systemName: "sidebar.left") }.help("显示或隐藏缩略图")
                Text(session.publication?.title ?? "").font(.headline).lineLimit(1)
            } else {
                Image(systemName: "water.waves").font(.title2).foregroundStyle(WaterTheme.accent)
                Text("水漫").font(.system(size: 21, weight: .semibold, design: .rounded))
                Text("翻开一页，漫入故事").font(.callout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 12)
            if session.publication != nil && session.tab == .reading {
                Picker("布局", selection: $session.preferences.layout) {
                    Text("单页").tag(ReadingLayout.single)
                    Text("双页").tag(ReadingLayout.double)
                }.pickerStyle(.segmented).labelsHidden().frame(width: 100).disabled(session.showWeb)
                    .onChange(of: session.preferences.layout) { _, _ in session.changedPreferences() }
                Picker("阅读方向", selection: $session.preferences.direction) {
                    Text("左 → 右").tag(ReadingDirection.ltr)
                    Text("右 → 左").tag(ReadingDirection.rtl)
                }.labelsHidden().frame(width: 92)
                    .onChange(of: session.preferences.direction) { _, _ in session.changedPreferences() }
                Toggle(isOn: $session.preferences.smartSpreads) { Label("智能跨页", systemImage: "rectangle.expand.vertical") }
                    .toggleStyle(.button).help("分析文字朝向与相邻画面，自动转正和完整展示跨页")
                    .onChange(of: session.preferences.smartSpreads) { _, _ in session.changedPreferences() }
                Button {session.toggleReadingControls()} label:{Image(systemName:"eye.slash")}.help("隐藏阅读控件 · Tab")
                pageMenu
            }
            Button { session.chooseFile() } label: { Label("打开", systemImage: "plus") }.buttonStyle(.bordered)
        }
        .buttonStyle(.borderless)
        .padding(.leading, 82).padding(.trailing, 20).frame(height: 64)
    }
    private var pageMenu: some View {
        Menu {
            Button("顺时针旋转 90°") { session.rotate(90) }
            Button("逆时针旋转 90°") { session.rotate(-90) }
            Divider()
            Button("与下一页组成跨页") { session.joinNext() }.disabled((session.publication?.units.count ?? 0) <= session.index+1 || session.showWeb)
            Button("交换双图左右") { session.swapPair() }.disabled(session.group?.indices.count != 2)
            Button("自动校正左右与接缝") { session.realignPair() }.disabled(session.group?.indices.count != 2 || session.showWeb)
            Button("取消跨页配对") { session.cancelPair() }.disabled(session.group?.indices.count != 2 || session.group?.spread != true)
            Button("这不是跨页") { session.disableSpread() }
            Button("清除当前页修正，恢复自动识别") { session.restorePage() }
            Divider()
            Toggle("自动合并双图", isOn: $session.preferences.automaticPairs)
                .onChange(of: session.preferences.automaticPairs) { _, _ in session.changedPreferences() }
            Toggle("积极合并（减少确认）", isOn: $session.preferences.aggressivePairs)
                .onChange(of: session.preferences.aggressivePairs) { _, _ in session.changedPreferences() }
            Toggle("自动转正（含普通单页）", isOn: $session.preferences.automaticOrientation)
                .onChange(of: session.preferences.automaticOrientation) { _, _ in session.changedPreferences() }
            Toggle("封面单独展示", isOn: $session.preferences.coverAlone)
                .onChange(of: session.preferences.coverAlone) { _, _ in session.changedPreferences() }
            if session.publication?.kind == .epub {
                Toggle("查看 EPUB 原版式", isOn: $session.originalLayout).onChange(of: session.originalLayout) { _, _ in session.render() }
            }
            Button("在 Finder 中显示") {
                if let url = session.publication?.sourceURL { NSWorkspace.shared.activateFileViewerSelecting([url]) }
            }
        } label: { Image(systemName: "ellipsis.circle").font(.title3) }.menuStyle(.borderlessButton).frame(width: 26)
        .help("页面修正与阅读设置")
    }
    private var library: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 28) {
                VStack(alignment: .leading, spacing: 12) {
                    Text(session.recent.isEmpty ? "下一段故事，从这里开始。" : "继续你的故事。")
                        .font(.system(size: 32, weight: .semibold, design: .rounded))
                    Text("图片、PDF、EPUB，拖进来就能读。跨页在这里完整展开。")
                        .font(.title3).foregroundStyle(.secondary)
                    Button { session.chooseFile() } label: {
                        Label("打开漫画或图片文件夹", systemImage: "folder.badge.plus").padding(.horizontal, 10).padding(.vertical, 5)
                    }.buttonStyle(.borderedProminent).controlSize(.large).padding(.top, 8)
                }.padding(.top, 26)
                if !session.recent.isEmpty {
                    HStack { Text("最近阅读").font(.title2.bold()); Spacer(); Text("\(session.recent.count) 本").foregroundStyle(.secondary) }
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 170, maximum: 220), spacing: 24)], alignment: .leading, spacing: 28) {
                        ForEach(session.recent) { book in
                            Button { session.openRecent(book) } label: { BookCard(book: book) }.buttonStyle(.plain)
                        }
                    }
                } else {
                    HStack(alignment: .top, spacing: 20) {
                        welcomeFeature("rectangle.on.rectangle", "按自己的方式读", "单页、双页、左右翻页\n支持捏合缩放与拖动")
                        welcomeFeature("arrow.up.left.and.arrow.down.right", "给跨页留足空间", "转正、完整展示\n双图组合随时可以纠正")
                        welcomeFeature("bookmark", "从上次的位置继续", "自动保存阅读位置\n记住每本漫画的偏好")
                    }.padding(.top, 26)
                }
                HStack(spacing: 18) {
                    Label("本地阅读", systemImage: "internaldrive")
                    Label("⌘O 打开", systemImage: "keyboard")
                    Label("方向键翻页", systemImage: "arrow.left.arrow.right")
                }.font(.callout).foregroundStyle(.secondary).padding(.top, 20)
                Spacer(minLength: 30)
            }.padding(.horizontal, 52).frame(maxWidth: 1140, alignment: .leading).frame(maxWidth: .infinity)
        }
    }
    private func welcomeFeature(_ icon: String, _ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            Image(systemName: icon).font(.system(size: 25, weight: .light)).foregroundStyle(WaterTheme.accent)
            Text(title).font(.headline)
            Text(detail).font(.callout).foregroundStyle(.secondary).lineSpacing(5)
        }.frame(maxWidth: .infinity, alignment: .leading).padding(24).background(Color.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 16))
    }
    private func reader(_ publication: Publication) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                if session.showSidebar && !immersive {
                    ThumbnailSidebar(session: session).id(publication.identity + publication.revision).frame(width: 178)
                    Divider()
                }
                ZStack {
                    Color(white: 0.075)
                    if session.showWeb, let unit = session.unit {
                        EPUBWebView(url: publication.sourceURL, resource: unit.locator.resource, revision: publication.revision)
                            .id(publication.identity + publication.revision)
                    } else {
                        ReaderCanvas(pages: session.pages, spread: session.group?.spread ?? false, verticalOffset: session.group?.verticalOffset ?? 0, rightScale:session.group?.rightScale ?? 1, zoom: session.zoom, fitWidth: session.fitWidth, reverseSwipe: session.preferences.direction == .rtl, edgeToEdge:immersive, onStep: { session.step($0) }, onZoom: { session.zoom = $0 })
                    }
                    if session.rendering { ProgressView().tint(.white).padding(20).background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 12)) }
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            if !immersive { Divider(); footer(publication) }
        }
        .contextMenu {
            Button(immersive ? "显示阅读控件 · Tab" : "隐藏阅读控件 · Tab") {session.toggleReadingControls()}
            Button("切换全屏 · ⌃⌘F") {session.toggleFullScreen()}
            Button("返回漫画库") {session.home()}
        }
    }
    private func footer(_ publication: Publication) -> some View {
        HStack(spacing: 14) {
            Button { session.toggleBookmark() } label: { Image(systemName: session.currentBookmarked ? "bookmark.fill" : "bookmark") }.help("书签 · ⌘D")
            Button { session.step(-1) } label: { Image(systemName: "chevron.left") }.help("上一组")
            Text(session.pageLabel).monospacedDigit().frame(minWidth: 84)
            Button { session.step(1) } label: { Image(systemName: "chevron.right") }.help("下一组")
            TextField("跳页", text: $session.jumpText).textFieldStyle(.roundedBorder).frame(width: 52).onSubmit { session.jump() }
            if session.canApplyAnalysis {
                Button("应用识别") { session.applyAnalysis() }.buttonStyle(.bordered)
            } else if session.pairSuggestion != nil {
                Button("可能是跨页 · 合并") { session.acceptPairSuggestion() }.buttonStyle(.bordered)
            } else if !session.status.isEmpty {
                Text(session.status).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            } else if !session.spreadMessage.isEmpty {
                Text(session.spreadMessage).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            if let next=session.nextVolume {
                Button("阅读下一卷") {session.openNextVolume(next)}.disabled(!next.available).help(next.available ? next.title : "下一卷文件不可用")
            }
            Spacer(minLength: 4)
            if !session.showWeb {
                Button { session.zoom = max(0.5, session.zoom/1.25) } label: { Image(systemName: "minus.magnifyingglass") }.help("缩小")
                Menu {
                    Button("适合窗口") { session.fitWidth = false; session.zoom = 1 }
                    Button("适合宽度") { session.fitWidth = true; session.zoom = 1 }
                    Button("放大 2 倍") { session.zoom = 2 }
                } label: { Text(session.zoom == 1 ? (session.fitWidth ? "适合宽度" : "适合窗口") : "\(Int(session.zoom*100))%") }.frame(width: 100)
                Button { session.zoom = min(6, session.zoom*1.25) } label: { Image(systemName: "plus.magnifyingglass") }.help("放大")
            } else { Text("原版式").font(.caption).foregroundStyle(.secondary) }
            Button { session.toggleFullScreen() } label: { Image(systemName: "arrow.up.left.and.arrow.down.right") }.help("全屏")
        }.buttonStyle(.borderless).padding(.horizontal, 20).frame(height: 52)
    }
}

private struct BookCard: View {
    let book: SavedBook
    var body: some View {
        VStack(alignment:.leading,spacing:9) {
            CatalogCover(path:book.path,revision:book.revision).frame(height:225)
            Text(book.title).font(.headline).lineLimit(2).frame(height:38,alignment:.top)
            Text(book.openedAt,style:.relative).font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct ThumbnailSidebar: View {
    @ObservedObject var session: ReaderSession
    @State private var section = 0
    var body: some View {
        VStack(spacing: 0) {
            Picker("导航", selection: $section) { Text("页面").tag(0); Text("书签").tag(1); Text("目录").tag(2) }
                .pickerStyle(.segmented).labelsHidden().padding(12)
            if section == 2 {
                if session.publication?.navigation.isEmpty == true { Text("这本书没有可用目录\n可使用页面缩略图导航").font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center).padding(.top, 30); Spacer() }
                else {
                    List(session.publication?.navigation ?? []) { item in Button(item.title) { session.go(to: item.index) }.buttonStyle(.plain) }
                }
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 12) {
                            if let publication = session.publication {
                                ForEach(Array(publication.units.enumerated()), id: \.element.id) { i, unit in
                                    if section == 0 || session.bookmarks.contains(unit.locator) {
                                        Button { session.go(to: i) } label: {
                                            ThumbnailRow(engine: session.engine, index: i, unit: unit, selected: session.group?.indices.contains(i) == true)
                                        }.buttonStyle(.plain).id(i)
                                    }
                                }
                            }
                        }.padding(.horizontal, 18).padding(.bottom, 18)
                    }
                    .onChange(of: session.index) { _, value in withAnimation(.easeOut(duration: 0.15)) { proxy.scrollTo(value, anchor: .center) } }
                    .onAppear { proxy.scrollTo(session.index, anchor: .center) }
                }
            }
        }.background(Color(nsColor: .controlBackgroundColor))
    }
}

private struct ThumbnailRow: View {
    let engine: DocumentEngine?
    let index: Int
    let unit: ReadingUnit
    let selected: Bool
    @State private var image: NSImage?
    var body: some View {
        VStack(spacing: 6) {
            ZStack {
                RoundedRectangle(cornerRadius: 4).fill(Color.primary.opacity(0.04))
                if let image { Image(nsImage: image).resizable().scaledToFit().padding(4) }
                else { Image(systemName: unit.complex ? "doc.richtext" : "photo").foregroundStyle(.secondary) }
            }.frame(height: 158)
                .overlay(RoundedRectangle(cornerRadius: 5).stroke(selected ? WaterTheme.accent : .clear, lineWidth: 2))
            HStack { Text("\(index+1)").monospacedDigit(); if unit.isCover { Text("封面") }; Spacer() }.font(.caption).foregroundStyle(selected ? .primary : .secondary)
        }.contentShape(Rectangle())
        .task(id: unit.id) {
            image = nil
            if let cg = try? await engine?.image(at: index, maxPixel: 240), !Task.isCancelled { image = NSImage(cgImage: cg, size: .zero) }
        }
        .accessibilityLabel("第 \(index+1) 页，\(unit.title)")
    }
}
