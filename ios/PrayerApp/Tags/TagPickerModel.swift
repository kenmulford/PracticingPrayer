import Foundation
import Observation
import PrayerKit

@Observable
final class TagPickerModel {
    enum LoadState: Equatable { case idle, loaded, failed }

    private(set) var tags: [PrayerTagRecord] = []
    private(set) var loadState: LoadState = .idle
    private(set) var isCreating = false
    private(set) var createFailed = false

    func load(using store: TagStore) {
        do {
            tags = try store.tags().map(\.tag).filter { $0.isSystem != true }
            loadState = .loaded
        } catch {
            loadState = .failed
        }
    }

    func visibleTags(for searchText: String) -> [PrayerTagRecord] {
        let trimmed = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return searchText.isEmpty ? tags : [] }
        return tags.filter { $0.name.localizedCaseInsensitiveContains(trimmed) }
    }

    func createCandidate(for searchText: String) -> String? {
        let trimmed = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        let exists = tags.contains { $0.name.caseInsensitiveCompare(trimmed) == .orderedSame }
        return exists ? nil : trimmed
    }

    func create(name: String, using store: TagStore) async -> PrayerTagRecord? {
        guard !isCreating else { return nil }
        isCreating = true
        createFailed = false
        defer { isCreating = false }
        let color = UserColorStore.defaultPaletteHexValues[0]
        do {
            let record = try await Task.detached {
                try store.createTag(name: name, color: color, now: Date())
            }.value
            tags.append(record)
            tags.sort { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
            return record
        } catch {
            createFailed = true
            if error as? TagError == .duplicateName { load(using: store) }
            return nil
        }
    }

    func clearCreateError() {
        createFailed = false
    }
}
