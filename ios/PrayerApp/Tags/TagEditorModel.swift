import Accessibility
import Foundation
import Observation
import PrayerKit

@Observable
final class TagEditorModel {
    enum Alert: Equatable {
        case duplicateName(String)
        case failure(String)
    }

    struct Swatch: Identifiable, Equatable {
        let id: Int64
        let hex: String
        let isDefault: Bool
    }

    @ObservationIgnored private let database: PrayerDatabase
    @ObservationIgnored private let original: PrayerTagRecord?
    @ObservationIgnored private let originalName: String
    @ObservationIgnored private var originalHex: String?

    var name: String
    var selectedHex: String?
    private(set) var swatches: [Swatch] = []
    var alert: Alert?
    var pendingColorDelete: Swatch?

    init(database: PrayerDatabase, tag: PrayerTagRecord?) {
        self.database = database
        self.original = tag
        self.originalName = tag?.name ?? ""
        self.name = tag?.name ?? ""
        reloadSwatches()
        if let color = tag?.color {
            self.selectedHex = swatches.first { $0.hex.caseInsensitiveCompare(color) == .orderedSame }?.hex
        } else if tag == nil {
            self.selectedHex = UserColorStore.defaultPaletteHexValues[0]
        }
        self.originalHex = selectedHex
    }

    var isNew: Bool { original == nil }
    var isNameEditable: Bool { original?.isSystem != true }
    var canDelete: Bool { !isNew && isNameEditable }

    private var trimmedName: String { name.trimmingCharacters(in: .whitespacesAndNewlines) }

    var canSave: Bool {
        guard isNew else { return isNameEditable ? !trimmedName.isEmpty : true }
        return !trimmedName.isEmpty && selectedHex != nil
    }

    var isDirty: Bool { name != originalName || selectedHex != originalHex }

    static func colorName(forHex hex: String) -> String {
        if let index = UserColorStore.defaultPaletteHexValues.firstIndex(where: {
            $0.caseInsensitiveCompare(hex) == .orderedSame
        }) {
            return defaultNames[index]
        }
        return approximateName(forHex: hex)
    }

    private static let defaultNames = [
        String(localized: "Red"), String(localized: "Orange"), String(localized: "Brown"),
        String(localized: "Teal"), String(localized: "Blue"), String(localized: "Purple"),
        String(localized: "Pink"), String(localized: "Gray"),
    ]

    private static func approximateName(forHex hex: String) -> String {
        guard let (hue, saturation, value) = hsv(forHex: hex) else { return String(localized: "Custom") }
        if value < 0.15 { return String(localized: "Black") }
        if saturation < 0.10 {
            if value > 0.85 { return String(localized: "White") }
            return value > 0.5 ? String(localized: "Light gray") : String(localized: "Dark gray")
        }
        let base: String
        switch hue {
        case ..<15: base = String(localized: "Red")
        case ..<40: base = String(localized: "Orange")
        case ..<70: base = String(localized: "Yellow")
        case ..<160: base = String(localized: "Green")
        case ..<195: base = String(localized: "Teal")
        case ..<250: base = String(localized: "Blue")
        case ..<290: base = String(localized: "Purple")
        case ..<330: base = String(localized: "Pink")
        default: base = String(localized: "Red")
        }
        if value < 0.4 { return String(localized: "Dark \(base)") }
        if saturation < 0.25 && value > 0.8 { return String(localized: "Pastel \(base)") }
        if saturation < 0.5 { return String(localized: "Light \(base)") }
        return base
    }

    private static func hsv(forHex hex: String) -> (hue: Double, saturation: Double, value: Double)? {
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard digits.count == 6, let raw = UInt32(digits, radix: 16) else { return nil }
        let r = Double((raw >> 16) & 0xFF) / 255
        let g = Double((raw >> 8) & 0xFF) / 255
        let b = Double(raw & 0xFF) / 255
        let maxC = max(r, g, b), minC = min(r, g, b)
        let delta = maxC - minC
        var hue = 0.0
        if delta > 0 {
            if maxC == r {
                hue = 60 * (((g - b) / delta).truncatingRemainder(dividingBy: 6))
            } else if maxC == g {
                hue = 60 * ((b - r) / delta + 2)
            } else {
                hue = 60 * ((r - g) / delta + 4)
            }
            if hue < 0 { hue += 360 }
        }
        return (hue, maxC == 0 ? 0 : delta / maxC, maxC)
    }

    func label(for swatch: Swatch) -> String {
        let name = Self.colorName(forHex: swatch.hex)
        let shared = swatches.filter { Self.colorName(forHex: $0.hex) == name }
        guard shared.count > 1, let index = shared.firstIndex(of: swatch) else { return name }
        return String(localized: "\(name) \(index + 1) of \(shared.count)")
    }

    func tagsUsing(_ swatch: Swatch) -> Int {
        let summaries = (try? TagStore(database: database).tags()) ?? []
        return summaries.filter { $0.tag.color?.caseInsensitiveCompare(swatch.hex) == .orderedSame }.count
    }

    func addCustomColor(hex: String, now: Date = .now) {
        do {
            let record = try UserColorStore(database: database).saveColor(hex: hex, now: now)
            reloadSwatches()
            selectedHex = record.hexValue
        } catch {
            alert = .failure(error.localizedDescription)
        }
    }

    func deleteColor(_ swatch: Swatch, replacementHex: String, now: Date = .now) -> Bool {
        do {
            try UserColorStore(database: database).deleteColor(
                id: swatch.id, replacementHex: replacementHex, now: now)
            reloadSwatches()
            if selectedHex?.caseInsensitiveCompare(swatch.hex) == .orderedSame {
                selectedHex = swatches.first { $0.hex.caseInsensitiveCompare(replacementHex) == .orderedSame }?.hex
            }
            return true
        } catch {
            alert = .failure(error.localizedDescription)
            return false
        }
    }

    func save(now: Date = .now) -> Bool {
        let store = TagStore(database: database)
        do {
            if let original {
                if isNameEditable, trimmedName != original.name {
                    try store.renameTag(id: original.id, to: trimmedName, now: now)
                }
                if let selectedHex, selectedHex != originalHex {
                    try store.recolorTag(id: original.id, to: selectedHex, now: now)
                }
            } else if let selectedHex {
                _ = try store.createTag(name: trimmedName, color: selectedHex, now: now)
            }
            AccessibilityNotification.Announcement(String(localized: "Tag saved")).post()
            return true
        } catch TagError.duplicateName {
            alert = .duplicateName(trimmedName)
            return false
        } catch {
            alert = .failure(error.localizedDescription)
            return false
        }
    }

    func delete() -> Bool {
        guard let original else { return false }
        do {
            try TagStore(database: database).deleteTag(id: original.id)
            return true
        } catch {
            alert = .failure(error.localizedDescription)
            return false
        }
    }

    private func reloadSwatches() {
        let records = (try? UserColorStore(database: database).colors()) ?? []
        swatches = records.map { Swatch(id: $0.id, hex: $0.hexValue, isDefault: $0.isDefault) }
    }
}
