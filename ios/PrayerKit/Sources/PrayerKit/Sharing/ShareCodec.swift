import Compression
import Foundation

public struct SharedPrayer: Equatable, Sendable {
    public let title: String
    public let notes: String?

    public init(title: String, notes: String?) {
        self.title = title
        self.notes = notes
    }
}

public enum SharePayload: Equatable, Sendable {
    case request(SharedPrayer)
    case card(title: String, requests: [SharedPrayer])
}

public enum ShareDecodeResult: Equatable, Sendable {
    case payload(SharePayload)
    case newerVersion
    case invalid
    case empty
    case notAShareLink
}

public enum ShareEncoding: Equatable, Sendable {
    case link(String)
    /// Raw gzip JSON, the `.prayercard` file body.
    case file(Data)
}

/// Port of the MAUI `DeepLinkService` wire format; `contracts/share` holds the cross-platform fixtures.
public enum ShareCodec {
    public static let maxLinkLength = 1800
    /// Largest `.prayercard` file `decode(file:)` accepts; callers check the size before reading it.
    public static let maxFileBytes = Gzip.maxShareBytes

    private static let shareHost = "practicingprayerapp.com"
    private static let baseURL = "https://practicingprayerapp.com/share"

    private struct WireRequest: Codable {
        var title: String?
        var notes: String?
    }

    private struct WireCard: Codable {
        var title: String?
        var requests: [WireRequest?]?
    }

    // MARK: Encode

    public static func encode(_ payload: SharePayload) throws -> ShareEncoding {
        let gzip = try Gzip.compress(json(for: payload))
        let path: String
        switch payload {
        case .request: path = "r"
        case .card: path = "c"
        }
        let link = "\(baseURL)/\(path)?v=2&d=z.\(base64URL(gzip))"
        return link.count <= maxLinkLength ? .link(link) : .file(gzip)
    }

    public static func encode(request: PrayerRequestRecord) throws -> ShareEncoding {
        try encode(.request(SharedPrayer(title: request.title, notes: request.details)))
    }

    public static func encode(card: PrayerCardRecord, prayers: [PrayerRequestRecord]) throws -> ShareEncoding {
        try encode(.card(
            title: card.title,
            requests: prayers.map { SharedPrayer(title: $0.title, notes: $0.details) }))
    }

    static func json(for payload: SharePayload) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        func wire(_ prayer: SharedPrayer) -> WireRequest {
            WireRequest(
                title: TextNormalization.normalizeQuotes(prayer.title),
                notes: prayer.notes.map(TextNormalization.normalizeQuotes) ?? "")
        }
        switch payload {
        case .request(let prayer):
            return try encoder.encode(wire(prayer))
        case .card(let title, let requests):
            return try encoder.encode(WireCard(
                title: TextNormalization.normalizeQuotes(title),
                requests: requests.map(wire)))
        }
    }

    // MARK: Decode

    public static func decode(file: Data) -> ShareDecodeResult {
        guard let json = try? Gzip.decompress(file),
              let root = try? JSONSerialization.jsonObject(with: json) as? [String: Any]
        else { return .invalid }
        do {
            if root["requests"] != nil {
                let card = try JSONDecoder().decode(WireCard.self, from: json)
                return stageCard(title: card.title, requests: card.requests)
            }
            let request = try JSONDecoder().decode(WireRequest.self, from: json)
            return stageRequest(title: request.title, notes: request.notes)
        } catch {
            return .invalid
        }
    }

    public static func decode(link: String) -> ShareDecodeResult {
        guard let components = URLComponents(string: link),
              components.host?.lowercased() == shareHost
        else { return .notAShareLink }

        let query = parseQuery(components.percentEncodedQuery)
        if let version = query["v"].flatMap({ Int($0.trimmingCharacters(in: .whitespaces)) }), version > 2 {
            return .newerVersion
        }

        var path = components.path
        while path.hasSuffix("/") { path.removeLast() }
        guard path == "/share/r" || path == "/share/c" else { return .notAShareLink }
        let isRequest = path == "/share/r"

        if let payload = query["d"], !isBlank(payload) {
            do {
                let json = try decodePayload(payload)
                if isRequest {
                    let request = try JSONDecoder().decode(WireRequest.self, from: json)
                    return stageRequest(title: request.title, notes: request.notes)
                }
                let card = try JSONDecoder().decode(WireCard.self, from: json)
                return stageCard(title: card.title, requests: card.requests)
            } catch {
                return .invalid
            }
        }

        if isRequest {
            guard let title = query["title"], !isBlank(title) else { return .notAShareLink }
            return stageRequest(title: title, notes: query["notes"])
        }
        guard let requestsParam = query["requests"], !isBlank(requestsParam) else { return .notAShareLink }
        do {
            guard let json = fromBase64URL(requestsParam) else { return .invalid }
            let requests = try JSONDecoder().decode([WireRequest?].self, from: json)
            return stageCard(title: query["title"], requests: requests)
        } catch {
            return .invalid
        }
    }

    // MARK: Staging

    private static func stageRequest(title: String?, notes: String?) -> ShareDecodeResult {
        guard let title, !isBlank(title) else { return .empty }
        return .payload(.request(SharedPrayer(
            title: TextNormalization.normalizeQuotes(title),
            notes: normalizedNotes(notes))))
    }

    private static func stageCard(title: String?, requests: [WireRequest?]?) -> ShareDecodeResult {
        guard let title, !isBlank(title) else { return .empty }
        let prayers = (requests ?? []).compactMap { request -> SharedPrayer? in
            guard let request, let requestTitle = request.title, !isBlank(requestTitle) else { return nil }
            return SharedPrayer(
                title: TextNormalization.normalizeQuotes(requestTitle),
                notes: normalizedNotes(request.notes))
        }
        guard !prayers.isEmpty else { return .empty }
        return .payload(.card(title: TextNormalization.normalizeQuotes(title), requests: prayers))
    }

    private static func normalizedNotes(_ notes: String?) -> String? {
        guard let notes, !notes.isEmpty else { return nil }
        return TextNormalization.normalizeQuotes(notes)
    }

    private static func isBlank(_ text: String) -> Bool {
        text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    // MARK: Helpers

    /// Splits like `HttpUtility.ParseQueryString`: `+` is a space, first occurrence of a key wins.
    private static func parseQuery(_ percentEncodedQuery: String?) -> [String: String] {
        var result: [String: String] = [:]
        for pair in (percentEncodedQuery ?? "").split(separator: "&", omittingEmptySubsequences: true) {
            guard let equals = pair.firstIndex(of: "=") else { continue }
            let key = decodeQueryComponent(pair[..<equals])
            let value = decodeQueryComponent(pair[pair.index(after: equals)...])
            if result[key] == nil { result[key] = value }
        }
        return result
    }

    private static func decodeQueryComponent(_ raw: Substring) -> String {
        let spaced = raw.replacingOccurrences(of: "+", with: " ")
        return spaced.removingPercentEncoding ?? spaced
    }

    /// Drops trailing characters that messaging apps append when they URL-encode the fallback text into `d=`.
    private static func decodePayload(_ payload: String) throws -> Data {
        let allowed = Set("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.")
        let prefix = String(payload.prefix { allowed.contains($0) })
        if prefix.hasPrefix("z.") {
            guard let compressed = fromBase64URL(String(prefix.dropFirst(2))) else { throw GzipError.invalid }
            return try Gzip.decompress(compressed)
        }
        guard let data = fromBase64URL(prefix) else { throw GzipError.invalid }
        return data
    }

    static func base64URL(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private static func fromBase64URL(_ text: String) -> Data? {
        var base64 = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        switch base64.count % 4 {
        case 2: base64 += "=="
        case 3: base64 += "="
        default: break
        }
        return Data(base64Encoded: base64)
    }
}

enum GzipError: Error {
    case invalid
}

/// COMPRESSION_ZLIB emits raw DEFLATE, so the RFC 1952 header and trailer are written by hand.
enum Gzip {
    private static let crcTable: [UInt32] = (0..<256).map { index in
        var crc = UInt32(index)
        for _ in 0..<8 { crc = crc & 1 == 1 ? 0xEDB8_8320 ^ (crc >> 1) : crc >> 1 }
        return crc
    }

    static func crc32(_ data: Data) -> UInt32 {
        ~data.reduce(UInt32.max) { crcTable[Int(($0 ^ UInt32($1)) & 0xFF)] ^ ($0 >> 8) }
    }

    private static func littleEndian(_ value: UInt32) -> [UInt8] {
        (0..<4).map { UInt8((value >> (8 * UInt32($0))) & 0xFF) }
    }

    static func rawDeflate(_ data: Data) throws -> Data {
        try (data as NSData).compressed(using: .zlib) as Data
    }

    /// Streams the inflate and throws as soon as the output passes `maxOutputBytes`, so a hostile
    /// archive or link cannot grow memory past the cap. Truncated or corrupt input throws too.
    static func rawInflate(_ data: Data, maxOutputBytes: Int) throws -> Data {
        let chunkSize = 64 * 1024
        let scratch = UnsafeMutablePointer<UInt8>.allocate(capacity: chunkSize)
        defer { scratch.deallocate() }
        var stream = compression_stream(dst_ptr: scratch, dst_size: 0, src_ptr: scratch, src_size: 0, state: nil)
        guard compression_stream_init(&stream, COMPRESSION_STREAM_DECODE, COMPRESSION_ZLIB) != COMPRESSION_STATUS_ERROR else {
            throw GzipError.invalid
        }
        defer { compression_stream_destroy(&stream) }

        var output = Data()
        return try data.withUnsafeBytes { (input: UnsafeRawBufferPointer) -> Data in
            stream.src_ptr = input.bindMemory(to: UInt8.self).baseAddress ?? UnsafePointer(scratch)
            stream.src_size = input.count
            while true {
                // One byte past the cap is enough to prove the output is over it.
                let remaining = maxOutputBytes - output.count
                let room = remaining >= chunkSize ? chunkSize : remaining + 1
                stream.dst_ptr = scratch
                stream.dst_size = room
                let status = compression_stream_process(&stream, Int32(COMPRESSION_STREAM_FINALIZE.rawValue))
                let produced = room - stream.dst_size
                output.append(scratch, count: produced)
                guard output.count <= maxOutputBytes else { throw GzipError.invalid }
                switch status {
                case COMPRESSION_STATUS_END:
                    return output
                case COMPRESSION_STATUS_OK:
                    // No output and no input left means the stream ended early.
                    if produced == 0 && stream.src_size == 0 { throw GzipError.invalid }
                default:
                    throw GzipError.invalid
                }
            }
        }
    }

    static func compress(_ data: Data) throws -> Data {
        let deflated = try rawDeflate(data)
        var out = Data([0x1F, 0x8B, 0x08, 0x00, 0, 0, 0, 0, 0x00, 0xFF])
        out.append(deflated)
        out.append(contentsOf: littleEndian(crc32(data)))
        out.append(contentsOf: littleEndian(UInt32(truncatingIfNeeded: data.count)))
        return out
    }

    /// 1 MB. The 1.7.2 share path caps only the link length (`DeepLinkService.MaxUrlLength`, 1800),
    /// so this bound is native-side.
    static let maxShareBytes = 1_048_576

    static func decompress(_ data: Data, maxOutputBytes: Int = maxShareBytes) throws -> Data {
        guard data.count <= maxOutputBytes else { throw GzipError.invalid }
        let bytes = [UInt8](data)
        guard bytes.count >= 18, bytes[0] == 0x1F, bytes[1] == 0x8B, bytes[2] == 0x08 else {
            throw GzipError.invalid
        }
        let flags = bytes[3]
        var index = 10
        let end = bytes.count - 8
        if flags & 0x04 != 0 {
            guard index + 2 <= end else { throw GzipError.invalid }
            index += 2 + Int(bytes[index]) + Int(bytes[index + 1]) << 8
        }
        for flag: UInt8 in [0x08, 0x10] where flags & flag != 0 {
            while index < end, bytes[index] != 0 { index += 1 }
            index += 1
        }
        if flags & 0x02 != 0 { index += 2 }
        guard index <= end else { throw GzipError.invalid }

        let inflated = try rawInflate(Data(bytes[index..<end]), maxOutputBytes: maxOutputBytes)
        let trailer = Array(bytes[end...])
        guard Array(trailer[0..<4]) == littleEndian(crc32(inflated)),
              Array(trailer[4..<8]) == littleEndian(UInt32(truncatingIfNeeded: inflated.count))
        else { throw GzipError.invalid }
        return inflated
    }
}
