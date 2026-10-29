import Foundation
import PrayerKit
import SwiftUI
import Testing
@testable import PrayerApp

private let contractsURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db")

private func copyOf(_ fixture: String, named test: String) throws -> URL {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("TagPickerModelTests-\(test)-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: contractsURL.appendingPathComponent(fixture), to: copy)
    return copy
}

@MainActor
private func loadedModel(_ fixture: String, test: String) throws -> (TagPickerModel, TagStore, URL) {
    let copy = try copyOf(fixture, named: test)
    let store = TagStore(database: try PrayerDatabase(path: copy.path))
    let model = TagPickerModel()
    model.load(using: store)
    return (model, store, copy)
}

@MainActor
struct TagPickerModelTests {
    @Test func loadDropsSystemTagAndSorts() throws {
        let (model, _, copy) = try loadedModel("long-upgraded.db", test: "loadDropsSystemTagAndSorts")
        defer { try? FileManager.default.removeItem(at: copy) }
        #expect(model.tags.map(\.name) == ["Family", "Fixture Tag 1", "Fixture Tag 2", "Urgent", "Work"])
    }

    @Test func loadEmptyWhenOnlySystemTag() throws {
        let (model, _, copy) = try loadedModel("fresh-install.db", test: "loadEmptyWhenOnlySystemTag")
        defer { try? FileManager.default.removeItem(at: copy) }
        #expect(model.tags.isEmpty)
        #expect(model.loadState == .loaded)
    }

    @Test func loadFailureSetsFailed() throws {
        let empty = FileManager.default.temporaryDirectory
            .appendingPathComponent("TagPickerModelTests-loadFailureSetsFailed-\(UUID().uuidString).db")
        #expect(FileManager.default.createFile(atPath: empty.path, contents: Data()))
        defer { try? FileManager.default.removeItem(at: empty) }
        let model = TagPickerModel()
        model.load(using: TagStore(database: try PrayerDatabase(path: empty.path)))
        #expect(model.loadState == .failed)
    }

    @Test func searchIsCaseInsensitiveContains() throws {
        let (model, _, copy) = try loadedModel("long-upgraded.db", test: "searchIsCaseInsensitiveContains")
        defer { try? FileManager.default.removeItem(at: copy) }
        #expect(model.visibleTags(for: "fix").map(\.name) == ["Fixture Tag 1", "Fixture Tag 2"])
    }

    @Test func whitespaceSearchShowsNothingAndNoCandidate() throws {
        let (model, _, copy) = try loadedModel("long-upgraded.db", test: "whitespaceSearchShowsNothingAndNoCandidate")
        defer { try? FileManager.default.removeItem(at: copy) }
        #expect(model.visibleTags(for: "   ").isEmpty)
        #expect(model.createCandidate(for: "   ") == nil)
    }

    @Test func exactMatchHidesCandidate() throws {
        let (model, _, copy) = try loadedModel("long-upgraded.db", test: "exactMatchHidesCandidate")
        defer { try? FileManager.default.removeItem(at: copy) }
        #expect(model.createCandidate(for: " family ") == nil)
        #expect(model.createCandidate(for: " Grief ") == "Grief")
    }

    @Test func createAddsTagWithFirstDefaultColor() async throws {
        let (model, store, copy) = try loadedModel("long-upgraded.db", test: "createAddsTagWithFirstDefaultColor")
        defer { try? FileManager.default.removeItem(at: copy) }
        let record = try #require(await model.create(name: "Grief", using: store))
        #expect(record.name == "Grief")
        #expect(record.color == "#B84040")
        #expect(model.tags.contains(record))
    }

    @Test func duplicateCreateFailsAndReloads() async throws {
        let (model, store, copy) = try loadedModel("long-upgraded.db", test: "duplicateCreateFailsAndReloads")
        defer { try? FileManager.default.removeItem(at: copy) }
        let other = TagStore(database: try PrayerDatabase(path: copy.path))
        _ = try other.createTag(name: "Race", color: "#B84040", now: Date())
        let result = await model.create(name: "Race", using: store)
        #expect(result == nil)
        #expect(model.createFailed)
        #expect(model.tags.contains { $0.name == "Race" })
    }

    @Test func colorHexParses() {
        #expect(Color(hex: "#112233") != nil)
        #expect(Color(hex: "nope") == nil)
    }
}
