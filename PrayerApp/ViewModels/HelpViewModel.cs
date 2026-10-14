using PrayerApp.Services;
using System.Collections.ObjectModel;

namespace PrayerApp.ViewModels;

public class HelpViewModel
{
    public ObservableCollection<FaqItemViewModel> FaqItems { get; }

    public HelpViewModel(IAccessibilityService accessibilityService)
    {
        FaqItems = new ObservableCollection<FaqItemViewModel>
        {
            // Getting Started
            new("How do I create a prayer card?",
                "Tap the Prayer Cards tab, then tap \"Add Card\" in the top right. Give your card a name — a person, topic, or place — then tap Save, or Save + to save it and start another card in the same collection.",
                accessibilityService),

            new("How do I add a prayer request?",
                "Open a prayer card by tapping its name, then tap \"+ Add prayer\" at the bottom of the expanded card. Fill in a title and any details, then tap Save (to return to the card) or Save + to save and add another prayer request on the same card.",
                accessibilityService),

            new("What is Quick Add?",
                "Quick Add lets you jot down a prayer request from the home screen without choosing a card. It saves to a special \"Quick Add\" card that you can reorganize later.",
                accessibilityService),

            // Organization
            new("How do I organize prayers with tags?",
                "Tags are like labels you can attach to any prayer. Go to the Tags tab to create tags with custom colors, then add them to prayers from the prayer detail screen.",
                accessibilityService),

            new("Can I move a prayer to a different card?",
                "Yes. Open the prayer, tap Edit, then use the \"Belongs to Card\" picker to choose a different card. Tap Save when done.",
                accessibilityService),

            new("What does the star icon do?",
                "The star marks a card as a favorite. You can see this by swiping from left to right over a specific prayer card. Favorited cards sort to the top of your card list so they're always easy to find.",
                accessibilityService),

            // Prayer Time
            new("How does Prayer Time work?",
                "Prayer Time presents your prayers one at a time in a focused view. Swipe or tap the arrows to move between prayers. When you're done, tap \"I'm Done\" to end the session. By default, Prayer Time launches in landscape mode. You can change this in settings.",
                accessibilityService),

            new("What is auto-mode in Prayer Time?",
                "Auto-mode automatically advances to the next prayer after a set interval (30s, 60s, or 120s). Tap the timer display to cycle through intervals. Tap pause to stop auto-advance.",
                accessibilityService),

            new("Can I filter Prayer Time by tags?",
                "Yes. From the home screen, tap \"Prayer Time\" and choose \"By Tags.\" Select one or more tags, then tap Start to pray through only those tagged prayers.",
                accessibilityService),

            // Notifications
            new("How do I set up prayer reminders?",
                "Open a prayer request and enable the Reminders toggle. Choose a frequency (daily, weekly, or monthly), a time, and optionally a day of week. The app will send a notification at your chosen time.",
                accessibilityService),

            new("Why am I not receiving notifications?",
                "Check that notifications are enabled in App Settings and that your device's system settings allow notifications for this app. On iOS, also check Focus mode settings.",
                accessibilityService),

            // Data
            new("How do I back up my data?",
                "Go to Settings → Backup & Restore → Back Up Now. This creates a file you can save or share. To restore, use \"Restore from Backup\" on the same screen.",
                accessibilityService),

            new("Is my data private?",
                "Yes. All your data stays on your device. There are no accounts, no cloud sync, and no way for anyone else to see your prayers. Backups are files you control.",
                accessibilityService),
        };
    }
}
