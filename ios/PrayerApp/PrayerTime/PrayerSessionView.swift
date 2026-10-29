import PrayerKit
import SwiftUI

struct PrayerSessionView: View {
    @Environment(AppServices.self) private var services
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    let session: PrayerSession
    @State private var isAuto = false
    @State private var isPaused = false
    @State private var secondsRemaining = 0
    @State private var interval = 30

    init(session: PrayerSession) {
        self.session = session
    }

    var body: some View {
        NavigationStack {
            content
                .navigationBarTitleDisplayMode(.inline)
                .onAppear {
                    interval = PrayerSession.autoInterval(stored: services.preferences.autoModeIntervalSeconds)
                    AppDelegate.setPrayerSessionActive(true)
                    #if DEBUG
                    if ProcessInfo.processInfo.arguments.contains(PrayerSession.debugAutoArgument) {
                        startAuto()
                    }
                    #endif
                }
                .onDisappear { AppDelegate.setPrayerSessionActive(false) }
                .task(id: isAuto && !isPaused && !session.isComplete) {
                    guard isAuto && !isPaused && !session.isComplete else { return }
                    while true {
                        try? await Task.sleep(for: .seconds(1))
                        if Task.isCancelled { return }
                        secondsRemaining -= 1
                        if secondsRemaining <= 0 { animated { session.next() } }
                    }
                }
                .onChange(of: session.currentIndex) {
                    if isAuto { secondsRemaining = interval }
                }
                .onChange(of: session.isComplete) { _, complete in
                    if complete { isAuto = false; isPaused = false }
                }
                .onChange(of: scenePhase) { _, phase in
                    if phase == .background && isAuto { isPaused = true }
                }
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
                    ToolbarItem(placement: .topBarTrailing) {
                        autoMenu
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

    private var autoMenu: some View {
        Menu {
            Toggle("Auto Advance", isOn: Binding(get: { isAuto }, set: { $0 ? startAuto() : stopAuto() }))
                .disabled(session.isComplete)
                .accessibilityIdentifier("session.autoToggle")
            Picker("Interval", selection: Binding(get: { interval }, set: setInterval)) {
                Text("30 Seconds").tag(30)
                Text("1 Minute").tag(60)
                Text("2 Minutes").tag(120)
            }
            .accessibilityIdentifier("session.intervalPicker")
        } label: {
            Label("Auto Advance", systemImage: "timer")
        }
        .accessibilityLabel("Auto Advance")
        .accessibilityIdentifier("session.autoMenu")
    }

    private func startAuto() {
        isAuto = true
        isPaused = false
        secondsRemaining = interval
        AccessibilityNotification.Announcement(String(localized: "Auto-advance started")).post()
    }

    private func stopAuto() {
        isAuto = false
        isPaused = false
    }

    private func pauseAuto() {
        isPaused = true
        AccessibilityNotification.Announcement(String(localized: "Paused")).post()
    }

    private func resumeAuto() {
        secondsRemaining = interval
        isPaused = false
        AccessibilityNotification.Announcement(String(localized: "Resumed")).post()
    }

    private func setInterval(_ new: Int) {
        interval = new
        services.preferences.autoModeIntervalSeconds = new
        if isAuto { secondsRemaining = new }
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
        if isAuto {
            Button {
                isPaused ? resumeAuto() : pauseAuto()
            } label: {
                if isPaused {
                    Label("Resume", systemImage: "play.fill")
                } else {
                    Label("Pause", systemImage: "pause.fill")
                }
            }
            .accessibilityLabel(isPaused ? "Resume" : "Pause")
            .accessibilityIdentifier("session.pauseResume")
        }
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
