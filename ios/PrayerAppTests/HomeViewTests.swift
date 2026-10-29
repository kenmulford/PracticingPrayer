import Foundation
import PrayerKit
import SwiftUI
import Testing
@testable import PrayerApp

private let fixtureURL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appendingPathComponent("contracts/db/long-upgraded.db")

private func openFixture() throws -> (PrayerDatabase, URL) {
    let copy = FileManager.default.temporaryDirectory
        .appendingPathComponent("HomeViewTests-\(UUID().uuidString).db")
    try FileManager.default.copyItem(at: fixtureURL, to: copy)
    return (try PrayerDatabase(path: copy.path), copy)
}

@Test func columnCountDefaultsToTwo() {
    #expect(HomeModel.columnCount(dynamicTypeSize: .large, horizontalSizeClass: .compact) == 2)
}

@Test func columnCountAccessibilityIsOne() {
    #expect(HomeModel.columnCount(dynamicTypeSize: .accessibility1, horizontalSizeClass: .regular) == 1)
}

@Test func columnCountRegularIsFour() {
    #expect(HomeModel.columnCount(dynamicTypeSize: .large, horizontalSizeClass: .regular) == 4)
}

@Test func loadReadsFixtureMetrics() async throws {
    let (database, copy) = try openFixture()
    defer { try? FileManager.default.removeItem(at: copy) }
    let now = Date.now
    let model = HomeModel()
    await model.load { try database.homeMetrics(now: now, isSessionUnlocked: false) }
    let expected = try database.homeMetrics(now: now, isSessionUnlocked: false)
    #expect(model.metrics == expected)
    #expect(!model.loadFailed)
}

@Test func loadFailureKeepsZeroMetrics() async {
    let model = HomeModel()
    await model.load { throw PrayerDatabaseError.notADatabase }
    #expect(model.loadFailed)
    #expect(model.metrics.unanswered == 0)
    #expect(model.metrics.overdue.isEmpty)
    #expect(model.metrics.lastPrayed == nil)
}

@Test func retryAfterFailureClearsFlag() async throws {
    let (database, copy) = try openFixture()
    defer { try? FileManager.default.removeItem(at: copy) }
    let model = HomeModel()
    await model.load { throw PrayerDatabaseError.notADatabase }
    #expect(model.loadFailed)
    await model.load { try database.homeMetrics(now: .now, isSessionUnlocked: false) }
    #expect(!model.loadFailed)
}
