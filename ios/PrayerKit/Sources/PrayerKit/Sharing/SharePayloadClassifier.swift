import Foundation

/// Picks the shared text from whatever representation the host app delivered.
public enum SharePayloadClassifier {
    public static let maxPayloadBytes = 262_144

    public enum Result: Equatable, Sendable {
        case text(String)
        case failure(ImportBreadcrumbToken)
    }

    public static func classify(plainString: String?, attributedString: String?, rawBytes: Data?) -> Result {
        let text: String
        if let plainString {
            text = plainString
        } else if let attributedString {
            text = attributedString
        } else if let rawBytes {
            guard rawBytes.count <= maxPayloadBytes else { return .failure(.oversized) }
            guard let decoded = String(data: rawBytes, encoding: .utf8) else { return .failure(.unsupportedType) }
            text = decoded
        } else {
            return .failure(.unsupportedType)
        }
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return .failure(.emptyText) }
        return .text(text)
    }
}
