import Foundation
import PrayerKit
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

@MainActor
private func withModel(_ body: (TagsModel, TagStore) async throws -> Void) async throws {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("TagsModelTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    defer {
        for suffix in ["", "-journal", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: copy.path + suffix)
        }
    }
    let database = try PrayerDatabase(path: copy.path)
    defer { try? database.close() }
    let store = TagStore(database: database)
    try store.seedSystemTags(now: .now)
    let model = TagsModel(database: database)
    try await body(model, store)
}

@MainActor
@Suite struct TagsModelTests {
    private func summaries(_ model: TagsModel) throws -> [TagSummary] {
        try #require(model.load).get()
    }

    @Test func reloadOrdersSystemTagFirst() async throws {
        try await withModel { model, store in
            _ = try store.createTag(name: "Zeta TagsModelTests", color: "#112233", now: .now)
            _ = try store.createTag(name: "Alpha TagsModelTests", color: "#112233", now: .now)
            model.reload()
            let list = try summaries(model)
            #expect(list.first?.tag.isSystem == true)
            let names = list.map(\.tag.name)
            let alpha = try #require(names.firstIndex(of: "Alpha TagsModelTests"))
            let zeta = try #require(names.firstIndex(of: "Zeta TagsModelTests"))
            #expect(alpha < zeta)
        }
    }

    @Test func confirmDeleteRemovesTag() async throws {
        try await withModel { model, store in
            let tag = try store.createTag(name: "Doomed TagsModelTests", color: "#112233", now: .now)
            model.reload()
            model.pendingDelete = tag
            #expect(model.confirmDelete())
            #expect(model.summary(id: tag.id) == nil)
            #expect(model.pendingDelete == nil)
        }
    }

    @Test func confirmDeleteSystemTagKeepsRowAndSetsError() async throws {
        try await withModel { model, _ in
            model.reload()
            let system = try #require(try summaries(model).first { $0.tag.isSystem == true }).tag
            model.pendingDelete = system
            #expect(!model.confirmDelete())
            #expect(model.errorMessage != nil)
            #expect(model.summary(id: system.id) != nil)
        }
    }

    @Test func canDeleteIsFalseForSystemTag() async throws {
        try await withModel { model, store in
            let tag = try store.createTag(name: "Plain TagsModelTests", color: "#112233", now: .now)
            model.reload()
            let system = try #require(try summaries(model).first { $0.tag.isSystem == true }).tag
            #expect(!TagsModel.canDelete(system))
            #expect(TagsModel.canDelete(tag))
        }
    }
}
