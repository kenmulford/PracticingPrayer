using PrayerApp.Views;

namespace PrayerApp.Tests.Views;

public class CardPanelLayoutTests
{
    private static CardPanelDecision Decide(
        bool existing, int? selected, int? summary, bool list, bool summaryShown,
        bool opened = false, bool appearing = false) =>
        CardPanelLayout.Decide(existing, selected, summary, list, summaryShown, opened, appearing);

    // ── New-card mode ──────────────────────────

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NewCardMode_AnyPanelVisible_HidesBothAndClearsSummary(bool list, bool summaryShown)
    {
        var decision = Decide(existing: false, selected: 7, summary: 7, list, summaryShown);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.HideBoth, null), decision);
    }

    [Fact]
    public void NewCardMode_BothHidden_NoTransition()
    {
        var decision = Decide(existing: false, selected: 7, summary: 7, list: false, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.None, null), decision);
    }

    // ── Existing-card mode, no selection ──────────────────────────

    [Fact]
    public void Existing_NoSelection_ListHidden_ShowsListAndClearsSummary()
    {
        var decision = Decide(existing: true, selected: null, summary: null, list: false, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.ShowList, null), decision);
    }

    [Fact]
    public void Existing_SelectionClearedWhileSummaryShowing_ShowsListAndClearsSummary()
    {
        var decision = Decide(existing: true, selected: null, summary: 7, list: false, summaryShown: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.ShowList, null), decision);
    }

    [Fact]
    public void Existing_NoSelection_ListAlreadyOpen_NoTransition()
    {
        var decision = Decide(existing: true, selected: null, summary: 7, list: true, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.None, null), decision);
    }

    [Fact]
    public void Appearing_Existing_NoSelection_BothHidden_LeavesPanelsAlone()
    {
        var decision = Decide(existing: true, selected: null, summary: null, list: false, summaryShown: false, appearing: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.None, null), decision);
    }

    [Fact]
    public void Appearing_Existing_NoSelection_StaleSummaryShowing_ShowsList()
    {
        var decision = Decide(existing: true, selected: null, summary: 7, list: false, summaryShown: true, appearing: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.ShowList, null), decision);
    }

    // ── Existing-card mode, with a selection ──────────────────────────

    [Fact]
    public void Existing_KeptSelectionOnEntry_BothHidden_CollapsesToSummary()
    {
        var decision = Decide(existing: true, selected: 7, summary: null, list: false, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.CollapseToSummary, 7), decision);
    }

    [Fact]
    public void Existing_KeptSelectionOnEntry_SameIdAsStaleSummary_StillCollapses()
    {
        var decision = Decide(existing: true, selected: 7, summary: 7, list: true, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.CollapseToSummary, 7), decision);
    }

    [Fact]
    public void Existing_OtherCardPickedFromOpenList_CollapsesToTheNewCard()
    {
        var decision = Decide(existing: true, selected: 8, summary: 7, list: true, summaryShown: false);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.CollapseToSummary, 8), decision);
    }

    [Fact]
    public void Existing_ListOpenedByChange_SameCardRepointed_LeavesListOpen()
    {
        var decision = Decide(existing: true, selected: 7, summary: 7, list: true, summaryShown: false, opened: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.None, 7), decision);
    }

    [Fact]
    public void Existing_ListOpenedByChange_OtherCardPicked_CollapsesToTheNewCard()
    {
        var decision = Decide(existing: true, selected: 8, summary: 7, list: true, summaryShown: false, opened: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.CollapseToSummary, 8), decision);
    }

    [Fact]
    public void Existing_SummaryShowingSameCard_NoTransition()
    {
        var decision = Decide(existing: true, selected: 7, summary: 7, list: false, summaryShown: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.None, 7), decision);
    }

    [Fact]
    public void Existing_SummaryShowingOtherCard_SwapsSummaryCard()
    {
        var decision = Decide(existing: true, selected: 99, summary: 7, list: false, summaryShown: true);

        Assert.Equal(new CardPanelDecision(CardPanelTransition.SwapSummaryCard, 99), decision);
    }
}
