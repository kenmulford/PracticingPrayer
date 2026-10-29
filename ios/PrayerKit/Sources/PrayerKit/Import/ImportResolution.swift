import Foundation

public enum ImportTarget: Sendable, Equatable {
    case newCard
    case existingCard(id: Int64)
}

public struct ImportResolution: Sendable, Equatable {
    public let target: ImportTarget
    public private(set) var toImport: [SharedPrayer]
    public private(set) var alreadyOnCard: [SharedPrayer]
    public let lookupError: ImportResolverError?

    init(
        target: ImportTarget, toImport: [SharedPrayer], alreadyOnCard: [SharedPrayer] = [],
        lookupError: ImportResolverError? = nil
    ) {
        self.target = target
        self.toImport = toImport
        self.alreadyOnCard = alreadyOnCard
        self.lookupError = lookupError
    }

    public var isEmpty: Bool { toImport.isEmpty && alreadyOnCard.isEmpty }

    public var hasNothingToSave: Bool {
        !toImport.contains { !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }

    public var isAllDuplicates: Bool { !alreadyOnCard.isEmpty && hasNothingToSave }

    public mutating func moveToImport(at index: Int) {
        toImport.append(alreadyOnCard.remove(at: index))
    }
}
