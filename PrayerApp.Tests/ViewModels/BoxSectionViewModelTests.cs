using System.Collections.Specialized;
using NSubstitute;
using PrayerApp.Helpers;
using PrayerApp.Models;
using PrayerApp.Services;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class BoxSectionViewModelTests
{
    public BoxSectionViewModelTests()
    {
        // PrayerCardViewModel constructor needs these
        PrayerCard.SetDBService(Substitute.For<IDBService>());
    }

    private PrayerCardViewModel MakeCard(int id, string title) =>
        new(new PrayerCard { Id = id, Title = title },
            Substitute.For<ICardService>(),
            Substitute.For<IPrayerService>(),
            Substitute.For<IOnboardingService>(),
            Substitute.For<INavigationService>(),
            Substitute.For<IAccessibilityService>(),
            Substitute.For<IBoxService>(),
            Substitute.For<ISettings>(),
            Substitute.For<IConfidentialAccessService>());

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void UnboxedSection_UsesBoxStringsName()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);

        Assert.Equal(BoxStrings.Unorganized, section.Name);
        Assert.Equal(0, section.BoxId);
        Assert.False(section.IsSystem);
    }

    [Fact]
    public void BoxSection_UsesBoxName()
    {
        var box = new CardBox { Id = 5, Name = "Family", IsSystem = false };
        var section = new BoxSectionViewModel(box, defaultExpanded: true);

        Assert.Equal("Family", section.Name);
        Assert.Equal(5, section.BoxId);
        Assert.False(section.IsSystem);
    }

    // ── SetCards ───────────────────────────────────────────────────────

    [Fact]
    public void SetCards_WhenExpanded_PopulatesCollection()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);
        var cards = new[] { MakeCard(1, "A"), MakeCard(2, "B") };

        section.SetCards(cards);

        Assert.Equal(2, section.Count);
        Assert.Equal(2, section.CardCount);
    }

    [Fact]
    public void SetCards_WhenCollapsed_CollectionIsEmpty_ButCardCountReflectsBackingData()
    {
        var section = new BoxSectionViewModel(defaultExpanded: false);
        var cards = new[] { MakeCard(1, "A"), MakeCard(2, "B") };

        section.SetCards(cards);

        Assert.Empty(section); // Observable collection is empty (collapsed)
        Assert.Equal(2, section.CardCount); // Derived from backing list
    }

    // ── Slice 6b.2 — SetCards no-op when cards unchanged ──────────────

    [Fact]
    public void SetCards_IdenticalSequence_DoesNotFireResetNotification()
    {
        // 6b.2: when SetCards is called with the same card sequence as the current
        // _backingCards, ApplyExpansionState's Clear+Add cycle would fire a Reset
        // CollectionChanged event. On Android the Reset propagates to the grouped
        // CollectionView and re-inflates that section's cells — the per-section
        // cascade that survives 6b's BoxSections-replacement guard. SetCards must
        // short-circuit on identical input to eliminate this.
        var section = new BoxSectionViewModel(defaultExpanded: true);
        var card1 = MakeCard(1, "A");
        var card2 = MakeCard(2, "B");
        section.SetCards(new[] { card1, card2 });

        var resetCount = 0;
        section.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resetCount++;
        };

        section.SetCards(new[] { card1, card2 });

        Assert.Equal(0, resetCount);
    }

    // ── Expand / Collapse ─────────────────────────────────────────────

    [Fact]
    public void IsExpanded_Toggle_PopulatesAndClearsCollection()
    {
        var section = new BoxSectionViewModel(defaultExpanded: false);
        section.SetCards(new[] { MakeCard(1, "A") });

        Assert.Empty(section);

        section.IsExpanded = true;
        Assert.Single(section);

        section.IsExpanded = false;
        Assert.Empty(section);
    }

    // ── Filter expand / restore ───────────────────────────────────────

    [Fact]
    public void FilterExpand_ExpandsCollapsedSection()
    {
        var section = new BoxSectionViewModel(defaultExpanded: false);
        section.SetCards(new[] { MakeCard(1, "A") });
        Assert.False(section.IsExpanded);

        section.FilterExpand();

        Assert.True(section.IsExpanded);
        Assert.Single(section);
    }

    [Fact]
    public void RestoreUserExpansionState_RestoresAfterFilterExpand()
    {
        var section = new BoxSectionViewModel(defaultExpanded: false);
        section.SetCards(new[] { MakeCard(1, "A") });

        section.FilterExpand(); // auto-expanded by filter
        Assert.True(section.IsExpanded);

        section.RestoreUserExpansionState(); // user wanted it collapsed
        Assert.False(section.IsExpanded);
        Assert.Empty(section);
    }

    [Fact]
    public void RestoreUserExpansionState_NoOpWhenUserExpanded()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);
        section.SetCards(new[] { MakeCard(1, "A") });

        // User expands, filter runs, restore — should stay expanded
        section.RestoreUserExpansionState();
        Assert.True(section.IsExpanded);
    }

    [Fact]
    public void FilterExpand_NoOpWhenAlreadyExpanded()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);
        section.SetCards(new[] { MakeCard(1, "A") });

        section.FilterExpand();
        Assert.True(section.IsExpanded);

        // Restore should keep it expanded since user had it expanded
        section.RestoreUserExpansionState();
        Assert.True(section.IsExpanded);
    }

    // ── IsMultiSelectMode (BUG-59) ───────────────────────────────────

    [Fact]
    public void IsMultiSelectMode_RaisesPropertyChanged()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);
        var raised = false;
        ((System.ComponentModel.INotifyPropertyChanged)section).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoxSectionViewModel.IsMultiSelectMode)) raised = true;
        };

        section.IsMultiSelectMode = true;

        Assert.True(raised);
        Assert.True(section.IsMultiSelectMode);
    }

    [Fact]
    public void IsMultiSelectMode_DefaultFalse()
    {
        var section = new BoxSectionViewModel(defaultExpanded: true);

        Assert.False(section.IsMultiSelectMode);
    }

    // ── HeaderText ───────────────────────────────────────────────────

    [Fact]
    public void HeaderText_NoCards_ReturnsNameOnly()
    {
        var box = new CardBox { Id = 1, Name = "Family" };
        var section = new BoxSectionViewModel(box, defaultExpanded: true);

        Assert.Equal("Family", section.HeaderText);
    }

    [Fact]
    public void HeaderText_OneCard_ReturnsSingular()
    {
        var box = new CardBox { Id = 1, Name = "Family" };
        var section = new BoxSectionViewModel(box, defaultExpanded: true);
        section.SetCards(new[] { MakeCard(1, "A") });

        Assert.Equal("Family · 1 card", section.HeaderText);
    }

    [Fact]
    public void HeaderText_MultipleCards_ReturnsPlural()
    {
        var box = new CardBox { Id = 1, Name = "Family" };
        var section = new BoxSectionViewModel(box, defaultExpanded: true);
        section.SetCards(new[] { MakeCard(1, "A"), MakeCard(2, "B"), MakeCard(3, "C") });

        Assert.Equal("Family · 3 cards", section.HeaderText);
    }

    [Fact]
    public void HeaderText_UpdatesWhenCardsChange()
    {
        var box = new CardBox { Id = 1, Name = "Work" };
        var section = new BoxSectionViewModel(box, defaultExpanded: true);

        Assert.Equal("Work", section.HeaderText);

        section.SetCards(new[] { MakeCard(1, "A") });
        Assert.Equal("Work · 1 card", section.HeaderText);

        section.SetCards(new[] { MakeCard(1, "A"), MakeCard(2, "B") });
        Assert.Equal("Work · 2 cards", section.HeaderText);
    }
}
