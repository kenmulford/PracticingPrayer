import PrayerKit
import SwiftUI

struct PrayerSessionView: View {
    @Environment(AppServices.self) private var services
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    let session: PrayerSession

    init(session: PrayerSession) {
        self.session = session
    }

    var body: some View {
        NavigationStack {
            content
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        Button {
                            services.prayerSession = nil
                        } label: {
                            Label("Close", systemImage: "xmark")
                        }
                        .accessibilityLabel("Close")
                        .accessibilityIdentifier("PrayerSession_Close")
                    }
                    if !session.isComplete {
                        ToolbarItem(placement: .principal) {
                            Text("\(session.currentIndex + 1) of \(session.items.count)")
                                .monospacedDigit()
                                .accessibilityIdentifier("PrayerSession_Title")
                        }
                        ToolbarItemGroup(placement: .bottomBar) {
                            bottomBar
                        }
                    }
                }
        }
    }

    @ViewBuilder
    private var content: some View {
        if reduceMotion {
            page(at: session.currentIndex)
                .id(session.currentIndex)
                .transition(.opacity)
        } else {
            TabView(
                selection: Binding(get: { session.currentIndex }, set: { session.move(to: $0) })
            ) {
                ForEach(session.items.indices, id: \.self) { index in
                    PrayerSessionPage(item: session.items[index]).tag(index)
                }
                completion.tag(session.items.count)
            }
            .tabViewStyle(.page(indexDisplayMode: .never))
        }
    }

    @ViewBuilder
    private func page(at index: Int) -> some View {
        if index < session.items.count {
            PrayerSessionPage(item: session.items[index])
        } else {
            completion
        }
    }

    private var completion: some View {
        PrayerSessionCompletion { services.prayerSession = nil }
    }

    @ViewBuilder
    private var bottomBar: some View {
        let item = session.items[session.currentIndex]
        let answered = session.isAnswered(item)
        Button {
            animated { session.previous() }
        } label: {
            Label("Previous", systemImage: "chevron.left")
        }
        .disabled(session.isFirst)
        .accessibilityLabel("Previous")
        Button {
            animated { session.markAnswered() }
        } label: {
            if answered {
                Label("Answered", systemImage: "checkmark.circle.fill")
            } else {
                Label("Mark Answered", systemImage: "checkmark.circle")
            }
        }
        .disabled(answered)
        .sensoryFeedback(.success, trigger: session.answeredIds)
        .accessibilityLabel(answered ? "Answered" : "Mark Answered")
        Spacer()
        Button {
            animated { session.next() }
        } label: {
            Label("Next", systemImage: "chevron.right")
        }
        .buttonStyle(.glassProminent)
        .accessibilityLabel("Next")
    }

    private func animated(_ action: () -> Void) {
        if reduceMotion {
            withAnimation(.easeInOut, action)
        } else {
            action()
        }
    }
}

private struct PrayerSessionPage: View {
    let item: PrayerListItem

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text(item.cardTitle)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Text(item.record.title)
                    .font(.largeTitle.bold())
                if let details = item.record.details, !details.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text(details)
                        .font(.title3)
                }
            }
            .frame(maxWidth: 700, alignment: .leading)
            .padding(.horizontal)
            .frame(maxWidth: .infinity)
        }
    }
}

private struct PrayerSessionCompletion: View {
    let done: () -> Void

    var body: some View {
        ContentUnavailableView {
            Label("You've prayed through all your requests!", systemImage: "checkmark.circle")
        } actions: {
            Button("Done", action: done)
                .buttonStyle(.glassProminent)
        }
    }
}
