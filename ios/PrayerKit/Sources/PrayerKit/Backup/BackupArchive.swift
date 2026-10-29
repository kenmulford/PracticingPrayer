import Foundation

public enum BackupArchiveError: Error, Equatable {
    case invalidBackup
}

public struct BackupRestoreResult: Equatable, Sendable {
    public let confidentialPayload: Data?
}

/// The `.pcrd` backup container, a ZIP archive shared with the MAUI app (`BackupService`).
/// Hand-rolled so no third-party package is needed; DEFLATE and CRC-32 come from `Gzip`.
public enum BackupArchive {
    public static let databaseEntry = "prayer_app.db"
    public static let confidentialEntry = "confidential.enc"
    public static let maxEntryBytes = 256 * 1024 * 1024

    private static let restoreName = "prayer_app_restore.db"
    private static let backupName = "prayer_app_backup.tmp"
    private static let localSignature: UInt32 = 0x0403_4B50
    private static let centralSignature: UInt32 = 0x0201_4B50
    private static let endSignature: UInt32 = 0x0605_4B50
    private static let dosDate: UInt16 = 0x0021

    // MARK: Export

    /// Matches the MAUI name, `DateTime.Now.ToString("yyyy-MM-dd")`.
    public static func fileName(for date: Date, timeZone: TimeZone = .current) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd"
        return "practicing_prayer_\(formatter.string(from: date)).pcrd"
    }

    public static func export(databaseURL: URL, confidential: Data?, to exportDirectory: URL, now: Date = Date()) throws -> URL {
        let fileManager = FileManager.default
        try fileManager.createDirectory(at: exportDirectory, withIntermediateDirectories: true)
        for url in try fileManager.contentsOfDirectory(at: exportDirectory, includingPropertiesForKeys: nil)
        where url.pathExtension == "pcrd" {
            try fileManager.removeItem(at: url)
        }
        var items: [(name: String, data: Data)] = [(databaseEntry, try Data(contentsOf: databaseURL))]
        if let confidential { items.append((confidentialEntry, confidential)) }
        let destination = exportDirectory.appendingPathComponent(fileName(for: now))
        try archive(entries: items).write(to: destination, options: .atomic)
        return destination
    }

    // MARK: Restore

    public static func restore(
        archiveURL: URL,
        databaseURL: URL,
        upkeep: (PrayerDatabase) throws -> Void = { try SchemaUpkeep.run(on: $0) }
    ) throws -> BackupRestoreResult {
        let parsed: [String: Data]
        do {
            parsed = try entries(of: Data(contentsOf: archiveURL))
        } catch {
            throw BackupArchiveError.invalidBackup
        }
        guard let database = parsed[databaseEntry] else { throw BackupArchiveError.invalidBackup }

        let fileManager = FileManager.default
        let directory = databaseURL.deletingLastPathComponent()
        let restoreURL = directory.appendingPathComponent(restoreName)
        let backupURL = directory.appendingPathComponent(backupName)
        try? fileManager.removeItem(at: restoreURL)
        try? fileManager.removeItem(at: backupURL)
        try database.write(to: restoreURL)

        let hadLive = fileManager.fileExists(atPath: databaseURL.path)
        var opened: PrayerDatabase?
        do {
            if hadLive { try fileManager.moveItem(at: databaseURL, to: backupURL) }
            try fileManager.moveItem(at: restoreURL, to: databaseURL)
            let restored = try PrayerDatabase(path: databaseURL.path)
            opened = restored
            try upkeep(restored)
            try restored.close()
            opened = nil
        } catch {
            try? opened?.close()
            try? fileManager.removeItem(at: restoreURL)
            if hadLive, fileManager.fileExists(atPath: backupURL.path) {
                try? fileManager.removeItem(at: databaseURL)
                try? fileManager.moveItem(at: backupURL, to: databaseURL)
            }
            throw error
        }
        try? fileManager.removeItem(at: backupURL)
        return BackupRestoreResult(confidentialPayload: parsed[confidentialEntry])
    }

    // MARK: ZIP writer

    static func archive(entries items: [(name: String, data: Data)]) throws -> Data {
        var out = Data()
        var central = Data()
        for item in items {
            let name = Data(item.name.utf8)
            let crc = Gzip.crc32(item.data)
            let stored = item.data.isEmpty
            let payload = stored ? item.data : try Gzip.rawDeflate(item.data)
            let method: UInt16 = stored ? 0 : 8
            let offset = UInt32(out.count)

            out.appendLE(localSignature)
            out.appendLE(UInt16(20))
            out.appendLE(UInt16(0))
            out.appendLE(method)
            out.appendLE(UInt16(0))
            out.appendLE(dosDate)
            out.appendLE(crc)
            out.appendLE(UInt32(payload.count))
            out.appendLE(UInt32(item.data.count))
            out.appendLE(UInt16(name.count))
            out.appendLE(UInt16(0))
            out.append(name)
            out.append(payload)

            central.appendLE(centralSignature)
            central.appendLE(UInt16(20))
            central.appendLE(UInt16(20))
            central.appendLE(UInt16(0))
            central.appendLE(method)
            central.appendLE(UInt16(0))
            central.appendLE(dosDate)
            central.appendLE(crc)
            central.appendLE(UInt32(payload.count))
            central.appendLE(UInt32(item.data.count))
            central.appendLE(UInt16(name.count))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt32(0))
            central.appendLE(offset)
            central.append(name)
        }
        let centralOffset = UInt32(out.count)
        out.append(central)
        out.appendLE(endSignature)
        out.appendLE(UInt16(0))
        out.appendLE(UInt16(0))
        out.appendLE(UInt16(items.count))
        out.appendLE(UInt16(items.count))
        out.appendLE(UInt32(central.count))
        out.appendLE(centralOffset)
        out.appendLE(UInt16(0))
        return out
    }

    // MARK: ZIP reader

    /// Sizes, CRC, and offsets come from the central directory; the local header is used only to find
    /// where the data starts, because writers differ in the extra field they put there.
    static func entries(of archive: Data, maxEntryBytes: Int = maxEntryBytes) throws -> [String: Data] {
        let bytes = [UInt8](archive)
        func u16(_ at: Int) throws -> Int {
            guard at >= 0, at + 2 <= bytes.count else { throw BackupArchiveError.invalidBackup }
            return Int(bytes[at]) | Int(bytes[at + 1]) << 8
        }
        func u32(_ at: Int) throws -> Int {
            try u16(at) | u16(at + 2) << 16
        }

        guard bytes.count >= 22 else { throw BackupArchiveError.invalidBackup }
        var end = bytes.count - 22
        let lowest = max(0, bytes.count - 22 - 0xFFFF)
        while end >= lowest, try u32(end) != Int(endSignature) { end -= 1 }
        guard end >= lowest else { throw BackupArchiveError.invalidBackup }

        let count = try u16(end + 10)
        let centralSize = try u32(end + 12)
        var cursor = try u32(end + 16)
        guard count != 0xFFFF, centralSize != 0xFFFF_FFFF, cursor != 0xFFFF_FFFF,
              cursor + centralSize <= end
        else { throw BackupArchiveError.invalidBackup }

        var result = [String: Data]()
        for _ in 0..<count {
            guard try u32(cursor) == Int(centralSignature) else { throw BackupArchiveError.invalidBackup }
            let flags = try u16(cursor + 8)
            let method = try u16(cursor + 10)
            let crc = try u32(cursor + 16)
            let compressedSize = try u32(cursor + 20)
            let size = try u32(cursor + 24)
            let nameLength = try u16(cursor + 28)
            let extraLength = try u16(cursor + 30)
            let commentLength = try u16(cursor + 32)
            let localOffset = try u32(cursor + 42)
            guard cursor + 46 + nameLength <= bytes.count,
                  let name = String(data: Data(bytes[(cursor + 46)..<(cursor + 46 + nameLength)]), encoding: .utf8)
            else { throw BackupArchiveError.invalidBackup }
            cursor += 46 + nameLength + extraLength + commentLength

            guard flags & 0x01 == 0, method == 0 || method == 8,
                  size <= maxEntryBytes, size != 0xFFFF_FFFF, compressedSize != 0xFFFF_FFFF,
                  try u32(localOffset) == Int(localSignature), result[name] == nil
            else { throw BackupArchiveError.invalidBackup }
            let start = localOffset + 30 + (try u16(localOffset + 26)) + (try u16(localOffset + 28))
            guard start + compressedSize <= bytes.count else { throw BackupArchiveError.invalidBackup }
            let stored = Data(bytes[start..<(start + compressedSize)])

            let data: Data
            if method == 0 {
                data = stored
            } else {
                do {
                    data = size == 0 ? Data() : try Gzip.rawInflate(stored, maxOutputBytes: maxEntryBytes)
                } catch {
                    throw BackupArchiveError.invalidBackup
                }
            }
            guard data.count == size, Int(Gzip.crc32(data)) == crc else { throw BackupArchiveError.invalidBackup }
            if !name.hasSuffix("/") { result[name] = data }
        }
        return result
    }
}

private extension Data {
    mutating func appendLE(_ value: UInt16) {
        append(contentsOf: [UInt8(value & 0xFF), UInt8(value >> 8)])
    }

    mutating func appendLE(_ value: UInt32) {
        append(contentsOf: (0..<4).map { UInt8((value >> (8 * UInt32($0))) & 0xFF) })
    }
}
