import Foundation
import GRDB

public final class LibraryStore {
    private let database: DatabaseQueue
    public init(directory: URL? = nil) throws {
        let folder = directory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("ComicReader")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        database = try DatabaseQueue(path: folder.appendingPathComponent("Library.sqlite").path)
        var migrator = DatabaseMigrator()
        migrator.registerMigration("v1") { db in
            try db.execute(sql: "CREATE TABLE books (id TEXT PRIMARY KEY, snapshot BLOB NOT NULL, opened REAL NOT NULL)")
        }
        migrator.registerMigration("v2-catalog") { db in
            try db.execute(sql: "CREATE TABLE catalog (id INTEGER PRIMARY KEY, snapshot BLOB NOT NULL)")
        }
        try migrator.migrate(database)
    }
    public func books() throws -> [SavedBook] {
        try database.read { db in
            try Data.fetchAll(db, sql: "SELECT snapshot FROM books ORDER BY opened DESC").map { try JSONDecoder().decode(SavedBook.self, from: $0) }
        }
    }
    public func save(_ book: SavedBook) throws {
        let data = try JSONEncoder().encode(book)
        try database.write { db in
            try db.execute(sql: "INSERT INTO books (id,snapshot,opened) VALUES (?,?,?) ON CONFLICT(id) DO UPDATE SET snapshot=excluded.snapshot, opened=excluded.opened", arguments: [book.id, data, book.openedAt.timeIntervalSince1970])
        }
    }
    public func remove(_ id: String) throws {
        try database.write { db in try db.execute(sql: "DELETE FROM books WHERE id=?", arguments: [id]) }
    }
    public func catalog() throws -> Catalog {
        try database.read { db in
            guard let data=try Data.fetchOne(db,sql:"SELECT snapshot FROM catalog WHERE id=1") else { return Catalog() }
            return try JSONDecoder().decode(Catalog.self,from:data)
        }
    }
    public func saveCatalog(_ catalog: Catalog) throws {
        let data=try JSONEncoder().encode(catalog)
        try database.write { db in
            try db.execute(sql:"INSERT INTO catalog (id,snapshot) VALUES (1,?) ON CONFLICT(id) DO UPDATE SET snapshot=excluded.snapshot",arguments:[data])
        }
    }

}
