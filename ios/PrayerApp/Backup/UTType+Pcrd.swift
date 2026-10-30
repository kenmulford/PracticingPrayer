import UniformTypeIdentifiers

extension UTType {
    static let pcrd = UTType(filenameExtension: "pcrd", conformingTo: .zip) ?? .zip
}
