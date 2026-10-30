import CoreTransferable
import Foundation
import UniformTypeIdentifiers

extension UTType {
    nonisolated static let prayerCard = UTType(exportedAs: "com.multithreadedllc.prayercards.prayercard", conformingTo: .data)
}

nonisolated struct PrayerCardFile: Transferable, Equatable {
    let title: String
    let data: Data

    /// Leaves one `.prayercard` in `directory`, so a share never accumulates stale files.
    func write(to directory: URL) throws -> URL {
        let manager = FileManager.default
        for name in try manager.contentsOfDirectory(atPath: directory.path) where name.hasSuffix(".prayercard") {
            try manager.removeItem(at: directory.appendingPathComponent(name))
        }
        let url = directory.appendingPathComponent(ShareItemBuilder.sanitizedFileName(title) + ".prayercard")
        try data.write(to: url, options: .atomic)
        return url
    }

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(exportedContentType: .prayerCard, shouldAllowToOpenInPlace: false) { file in
            let caches = try FileManager.default.url(
                for: .cachesDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            return SentTransferredFile(try file.write(to: caches))
        }
    }
}
