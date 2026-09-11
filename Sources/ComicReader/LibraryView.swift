import SwiftUI
import ComicCore

struct LibraryView: View {
    @ObservedObject var model: LibraryModel
    @ObservedObject var session: ReaderSession
    @State private var search=""
    @State private var selectedSeries: String?
    @State private var selectedTag="全部"
    @State private var stateFilter="全部"
    @State private var compact=false
    @State private var editing: LibraryVolume?
    private var tags:[String] { ["全部","收藏"]+Array(Set(model.catalog.volumes.flatMap(\.tags))).filter{!["全部","收藏"].contains($0)}.sorted() }
    private var volumes:[LibraryVolume] {
        Catalog.sorted(model.catalog.volumes.filter { volume in
            (selectedSeries==nil || volume.seriesID==selectedSeries) &&
            (search.isEmpty || (volume.title+volume.series+volume.tags.joined()).localizedStandardContains(search)) &&
            (session.tab != .tags || selectedTag=="全部" || (selectedTag=="收藏" ? volume.favorite : volume.tags.contains(selectedTag))) &&
            (stateFilter=="全部" || volume.state.rawValue==stateFilter)
        })
    }
    private var groups:[(id:String,title:String,books:[LibraryVolume])] {
        Dictionary(grouping:volumes,by: \.seriesID).map { (id:$0.key,title:$0.value.first!.series,books:Catalog.sorted($0.value)) }
            .sorted { $0.title == $1.title ? $0.id < $1.id : $0.title.localizedStandardCompare($1.title) == .orderedAscending }
    }
    var body: some View {
        VStack(alignment:.leading,spacing:0) {
            if session.tab == .importing { importView }
            else {
                HStack {
                    if selectedSeries != nil { Button { selectedSeries=nil } label:{Label("全部系列",systemImage:"chevron.left")} }
                    Text(selectedSeries.flatMap { id in model.catalog.volumes.first{$0.seriesID==id}?.series } ?? (session.tab == .tags ? "标签与收藏" : "漫画库")).font(.largeTitle.bold())
                    Spacer()
                    TextField("搜索漫画、卷名或标签",text:$search).textFieldStyle(.roundedBorder).frame(width:240)
                    Toggle(isOn:$compact) { Image(systemName:"list.bullet") }.toggleStyle(.button).disabled(selectedSeries==nil && session.tab == .browse).help("卷列表显示方式")
                    Button {model.refresh()} label:{Image(systemName:"arrow.clockwise")}.disabled(model.scanning).help("更新目录")
                }.padding(.horizontal,36).padding(.top,26)
                HStack {
                    Text("\(groups.count) 个系列 · \(volumes.count) 卷").foregroundStyle(.secondary)
                    Spacer()
                    if session.tab == .tags {
                        Picker("标签",selection:$selectedTag) { ForEach(tags,id:\.self){Text($0)} }.frame(width:180)
                    }
                    Picker("阅读状态",selection:$stateFilter) { Text("全部").tag("全部");ForEach(ReadState.allCases,id:\.rawValue){Text($0.rawValue).tag($0.rawValue)} }.frame(width:170)
                }.padding(.horizontal,36).padding(.vertical,16)
                if volumes.isEmpty {
                    ContentUnavailableView {
                        Label(model.catalog.volumes.isEmpty ? "你的漫画库" : "没有匹配的漫画",systemImage:"books.vertical")
                    } description: {
                        Text(model.catalog.volumes.isEmpty ? "添加一个目录，按系列浏览收藏的漫画。" : "试试其他名称、标签或阅读状态。")
                    } actions: { Button("添加漫画目录") {model.chooseDirectory()} }
                } else {
                    ScrollView {
                        if selectedSeries==nil && session.tab == .browse {
                            if search.isEmpty { WaterCover().padding(.horizontal,36).padding(.top,8) }
                            LazyVGrid(columns:[GridItem(.adaptive(minimum:170,maximum:220),spacing:24)],alignment:.leading,spacing:28) {
                                ForEach(groups,id:\.id) { group in
                                    Button {selectedSeries=group.id} label: {
                                        VStack(alignment:.leading,spacing:9) {
                                            CatalogCover(path:group.books[0].path,revision:group.books[0].revision).frame(height:225)
                                            Text(group.title).font(.headline).lineLimit(1)
                                            Text("\(group.books.count) 卷 · \(group.books.filter{$0.state == .finished}.count) 已读").font(.caption).foregroundStyle(.secondary)
                                        }.contentShape(Rectangle())
                                    }.buttonStyle(.plain)
                                }
                            }.padding(36)
                        } else if compact {
                            LazyVStack(spacing:0) { ForEach(volumes) { volume in
                                HStack(spacing:16) {
                                    CatalogCover(path:volume.path,revision:volume.revision).frame(width:56,height:74)
                                    details(volume)
                                    Spacer()
                                    Button("编辑") {editing=volume}
                                    Button("阅读") {session.openVolume(volume)}.disabled(!volume.available)
                                }.padding(12)
                                Divider()
                            } }.padding(.horizontal,36)
                        } else {
                            LazyVGrid(columns:[GridItem(.adaptive(minimum:170,maximum:220),spacing:24)],alignment:.leading,spacing:28) {
                                ForEach(volumes) { volume in
                                    VStack(alignment:.leading,spacing:9) {
                                        Button {session.openVolume(volume)} label:{CatalogCover(path:volume.path,revision:volume.revision).frame(height:225)}.buttonStyle(.plain).disabled(!volume.available).accessibilityLabel("阅读 \(volume.title)")
                                        details(volume)
                                        HStack {
                                            Button {var v=volume;v.favorite.toggle();model.update(v)} label:{Image(systemName:volume.favorite ? "heart.fill":"heart")}.help("收藏")
                                            Spacer()
                                            Button("编辑") {editing=volume}
                                        }.buttonStyle(.borderless)
                                    }
                                }
                            }.padding(36)
                        }
                    }
                }
            }
        }.frame(maxWidth:.infinity,maxHeight:.infinity)
        .sheet(item:$editing) { volume in VolumeEditor(volume:volume,onSave:{ value in
            var revised=value
            if revised.seriesID.hasPrefix("manual:") {
                let matches=Set(model.catalog.volumes.filter{$0.path != value.path && $0.series==value.series}.map(\.seriesID))
                if matches.count==1 { revised.seriesID=matches.first! }
            }
            model.update(revised);editing=nil
        },onCancel:{editing=nil}) }
        .alert("书库操作失败",isPresented:Binding(get:{model.error != nil},set:{if !$0 {model.error=nil}})) { Button("好"){model.error=nil} } message:{Text(model.error ?? "")}
        .onChange(of:session.tab) { _,_ in selectedSeries=nil }
        .onChange(of:tags) { _,values in if !values.contains(selectedTag) { selectedTag="全部" } }
    }
    private func details(_ volume:LibraryVolume) -> some View {
        VStack(alignment:.leading,spacing:5) {
            Text(volume.title).font(.headline).lineLimit(2)
            Text(volume.available ? "\(volume.state.rawValue)\(volume.total>0 ? " · \(volume.position)/\(volume.total)" : "")" : "文件不可用").font(.caption).foregroundStyle(volume.available ? Color.secondary : Color.orange)
            if !volume.tags.isEmpty { Text(volume.tags.joined(separator:" · ")).font(.caption).foregroundStyle(WaterTheme.accent).lineLimit(1) }
        }
    }
    private var importView: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:24) {
                Text("把故事带进书库").font(.largeTitle.bold())
                Text("选择包含漫画的文件夹。子目录按系列归类，EPUB、PDF、MOBI 和图片文件夹都可以加入。原文件留在原处。")
                    .font(.title3).foregroundStyle(.secondary)
                HStack {
                    Button {model.chooseDirectory()} label:{Label("添加漫画目录",systemImage:"folder.badge.plus")}.buttonStyle(.borderedProminent).controlSize(.large).disabled(model.scanning)
                    Button("立即更新") {model.refresh()}.disabled(model.scanning || model.catalog.roots.isEmpty)
                    if model.scanning {ProgressView().controlSize(.small)}
                }
                Text(model.message).foregroundStyle(.secondary).textSelection(.enabled)
                Divider()
                Text("已添加的目录").font(.title2.bold())
                ForEach(model.catalog.roots) { root in
                    HStack {
                        Label(root.path,systemImage:"folder").textSelection(.enabled)
                        Spacer()
                        Button("停止扫描") {model.removeRoot(root)}.disabled(model.scanning)
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.quaternary,in:RoundedRectangle(cornerRadius:10))
                }
                Text("应用运行时每分钟检查新增与移除。不可访问的目录保留索引；不会删除漫画或阅读记录。图片与书籍混放的目录以 EPUB/PDF 为书卷，散落封面图不单独入库。")
                    .font(.callout).foregroundStyle(.secondary)
            }.padding(44).frame(maxWidth:1000,alignment:.leading).frame(maxWidth:.infinity)
        }
    }
}

private struct VolumeEditor: View {
    @State var volume:LibraryVolume
    let onSave:(LibraryVolume)->Void
    let onCancel:()->Void
    @State private var tags=""
    @State private var originalSeries=""
    var body:some View {
        VStack(alignment:.leading,spacing:16) {
            Text("编辑书卷").font(.title2.bold())
            TextField("卷名",text:$volume.title)
            TextField("系列名称",text:$volume.series)
            TextField("排序卷号",value:$volume.order,format:.number)
            TextField("标签，用逗号分隔",text:$tags)
            Picker("阅读状态",selection:$volume.state) {ForEach(ReadState.allCases,id:\.self){Text($0.rawValue).tag($0)}}
            Toggle("收藏",isOn:$volume.favorite)
            Text("同名手动系列会合并；不同版本请使用不同系列名。修改仅保存在书库。") .font(.caption).foregroundStyle(.secondary)
            HStack {Spacer();Button("取消",action:onCancel);Button("保存") {
                volume.series=volume.series.trimmingCharacters(in:.whitespacesAndNewlines)
                if volume.series != originalSeries { volume.seriesID="manual:"+volume.series };volume.customized=true
                volume.tags=Array(Set(tags.replacingOccurrences(of:"，",with:",").split(separator:",").map{$0.trimmingCharacters(in:.whitespacesAndNewlines)}.filter{!$0.isEmpty})).sorted()
                onSave(volume)
            }.buttonStyle(.borderedProminent).disabled(volume.series.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || volume.title.isEmpty || !volume.order.isFinite) }
        }.textFieldStyle(.roundedBorder).padding(28).frame(width:430)
        .onAppear{tags=volume.tags.joined(separator:", ");originalSeries=volume.series}
    }
}
