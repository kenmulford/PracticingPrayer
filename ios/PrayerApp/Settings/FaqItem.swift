import Foundation

struct FaqItem: Identifiable {
    let id: Int
    let question: LocalizedStringResource
    let answer: LocalizedStringResource

    static let all: [FaqItem] = [
        FaqItem(
            id: 1,
            question: "How do I create a prayer card?",
            answer: "Tap the Prayer Cards tab, then tap \"Add Card\" in the top right. Give your card a name \u{2014} a person, topic, or place \u{2014} then tap Save, or Save + to save it and start another card in the same collection."
        ),
        FaqItem(
            id: 2,
            question: "How do I add a prayer request?",
            answer: "Open a prayer card by tapping its name, then tap \"+ Add prayer\" at the bottom of the expanded card. Fill in a title and any details, then tap Save (to return to the card) or Save + to save and add another prayer request on the same card."
        ),
        FaqItem(
            id: 3,
            question: "What is Quick Add?",
            answer: "Quick Add lets you jot down a prayer request from the home screen without choosing a card. It saves to a special \"Quick Add\" card that you can reorganize later."
        ),
        FaqItem(
            id: 4,
            question: "How do I organize prayers with tags?",
            answer: "Tags are like labels you can attach to any prayer. Go to the Tags tab to create tags with custom colors, then add them to prayers from the prayer detail screen."
        ),
        FaqItem(
            id: 5,
            question: "Can I move a prayer to a different card?",
            answer: "Yes. Open the prayer, tap Edit, then use the \"Belongs to Card\" picker to choose a different card. Tap Save when done."
        ),
        FaqItem(
            id: 6,
            question: "What does the star icon do?",
            answer: "The star marks a card as a favorite. Swipe a card to the right in the Cards list, or touch and hold the card and choose Favorite. Favorited cards sort to the top of their collection so they're always easy to find."
        ),
        FaqItem(
            id: 7,
            question: "How does Prayer Time work?",
            answer: "Prayer Time presents your prayers one at a time in a focused view. Swipe or tap the arrows to move between prayers. When you're done, tap \"I'm Done\" to end the session. By default, Prayer Time launches in landscape mode. You can change this in settings."
        ),
        FaqItem(
            id: 8,
            question: "What is auto-mode in Prayer Time?",
            answer: "Auto-mode automatically advances to the next prayer after a set interval (30s, 60s, or 120s). Tap the timer display to cycle through intervals. Tap pause to stop auto-advance."
        ),
        FaqItem(
            id: 9,
            question: "Can I filter Prayer Time by tags?",
            answer: "Yes. From the home screen, tap \"Prayer Time\" and choose \"By Tags.\" Select one or more tags, then tap Start to pray through only those tagged prayers."
        ),
        FaqItem(
            id: 10,
            question: "How do I set up prayer reminders?",
            answer: "Open a prayer request and choose a reminder frequency: Never, Daily, Weekly, Monthly, or Yearly. Choose a time, and optionally a day of the week. The app will send a notification at your chosen time."
        ),
        FaqItem(
            id: 11,
            question: "Why am I not receiving notifications?",
            answer: "Check that notifications are enabled in App Settings and that your device's system settings allow notifications for this app. On iOS, also check Focus mode settings."
        ),
        FaqItem(
            id: 12,
            question: "How do I back up my data?",
            answer: "Go to Settings → Backup & Restore → Back Up Now. This creates a file you can save or share. To restore, use \"Restore from Backup\" on the same screen."
        ),
        FaqItem(
            id: 13,
            question: "Is my data private?",
            answer: "Yes. All your data stays on your device. There are no accounts, no cloud sync, and no way for anyone else to see your prayers. Backups are files you control."
        ),
    ]
}
