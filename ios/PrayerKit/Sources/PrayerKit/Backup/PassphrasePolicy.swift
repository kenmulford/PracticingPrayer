public enum PassphrasePolicy {
    public static let minLength = 12

    /// Counts UTF-16 units like C# `string.Length`, so a passphrase MAUI rejects is rejected here.
    public static func meetsMinimumLength(_ passphrase: String) -> Bool {
        passphrase.utf16.count >= minLength
    }
}
