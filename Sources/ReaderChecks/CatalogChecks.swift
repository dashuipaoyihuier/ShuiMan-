import Foundation
import ComicCore

extension ReaderChecks {
    static func catalogChecks() throws {
        let root=FileManager.default.temporaryDirectory.appendingPathComponent("CatalogChecks-"+UUID().uuidString)
        defer { try? FileManager.default.removeItem(at:root) }
        let series=root.appendingPathComponent("Series")
        let images=root.appendingPathComponent("Images/Volume 3")
        try FileManager.default.createDirectory(at:series,withIntermediateDirectories:true)
        try FileManager.default.createDirectory(at:images,withIntermediateDirectories:true)
        for name in ["Comic卷10.epub","Comic卷2.epub","Comic卷01.pdf","cover.jpg"] { try Data().write(to:series.appendingPathComponent(name)) }
        try Data().write(to:images.appendingPathComponent("1.png"))
        let initial=try LibraryScanner.scan(root)
        try expect(initial.count==4,"Catalog imports books and image folders without treating loose covers as books")
        let ordered=Catalog.sorted(initial.filter{$0.series=="Series"})
        try expect(ordered.map(\.order)==[1,2,10],"Catalog sorts volume numbers numerically")
        var catalog=Catalog();catalog.roots=[LibraryRoot(path:root.path)];catalog.merge(initial,root:root.path);catalog.merge(initial,root:root.path)
        try expect(catalog.volumes.count==4,"Repeated scans do not duplicate volumes")
        let path=ordered[1].path
        let i=catalog.volumes.firstIndex{$0.path==path}!
        catalog.volumes[i].tags=["待重读"];catalog.volumes[i].favorite=true;catalog.volumes[i].state = .finished;catalog.volumes[i].position=99
        catalog.volumes[i].title="Edited";catalog.volumes[i].customized=true
        catalog.merge(initial,root:root.path)
        try expect(catalog.volumes[i].title=="Edited" && catalog.volumes[i].favorite && catalog.volumes[i].position==99,"Rescan preserves manual metadata and reading progress")
        try expect(catalog.next(after:ordered[0].path)?.path==ordered[1].path,"Next volume follows series order")
        try FileManager.default.removeItem(atPath:path)
        catalog.merge(try LibraryScanner.scan(root),root:root.path)
        try expect(!catalog.volumes[i].available && catalog.volumes[i].tags==["待重读"],"Missing volumes retain tags and progress")
        try expect(catalog.next(after:ordered[0].path)?.available==false,"Next volume does not silently skip a missing installment")
        try Data().write(to:URL(fileURLWithPath:path));catalog.merge(try LibraryScanner.scan(root),root:root.path)
        try expect(catalog.volumes[i].available,"Returning files restore availability")
        let store=try LibraryStore(directory:root.appendingPathComponent("db"));try store.saveCatalog(catalog)
        let loaded=try LibraryStore(directory:root.appendingPathComponent("db")).catalog()
        try expect(loaded.volumes.count==4 && loaded.volumes[i].favorite && loaded.roots.count==1,"Catalog metadata survives SQLite reopen")
        let previousRevision=initial.first{$0.path==images.path}!.revision
        try Data([1,2,3]).write(to:images.appendingPathComponent("1.png"))
        let changed=try LibraryScanner.scan(root)
        try expect(changed.first{$0.path==images.path}!.revision != previousRevision,"Replacing an image invalidates the folder cover cache")
        try Data().write(to:series.appendingPathComponent("Comic卷11.epub"))
        catalog.merge(try LibraryScanner.scan(root),root:root.path)
        try expect(catalog.volumes.count==5 && catalog.next(after:ordered.last!.path)?.order==11,"New installments appear after incremental scan")
        var progress=ordered[0];progress.updateProgress(position:2,total:10);progress.state = .finished
        progress.updateProgress(position:2,total:10)
        try expect(progress.state == .finished,"Saving unchanged page does not undo an explicit read status")
        progress.updateProgress(position:3,total:10)
        try expect(progress.state == .reading && progress.position==3,"Advancing pages updates library progress")
        let last=catalog.volumes.first{$0.order==11}!.path
        try expect(catalog.next(after:last)==nil,"Last volume does not cross series boundaries")
    }
}
