namespace PrayerApp.Views;

internal enum CardPanelTransition { None, CollapseToSummary, ShowList, HideBoth, SwapSummaryCard }

internal readonly record struct CardPanelDecision(CardPanelTransition Transition, int? SummaryCardId);

/// <summary>
/// Decides how the Confirm Import card list and the selected-card summary move for the
/// view model's current state. Pure so every rule is unit-testable; the page only animates
/// the returned transition and stores <see cref="CardPanelDecision.SummaryCardId"/>.
/// </summary>
internal static class CardPanelLayout
{
    /// <param name="listOpenedByUser">
    /// True while the list is open because the user tapped "Change". A re-point to the same
    /// card (the re-lock reload) must not collapse a list the user reopened.
    /// </param>
    /// <param name="isAppearing">
    /// True for the page's OnAppearing pass. With no selection and the summary hidden it leaves
    /// the panels as they are: opening the list there would push the Quick Add prayer entry and
    /// its toolbar off screen.
    /// </param>
    internal static CardPanelDecision Decide(
        bool isExistingCardMode, int? selectedCardId, int? summaryCardId,
        bool listVisible, bool summaryVisible, bool listOpenedByUser, bool isAppearing)
    {
        if (!isExistingCardMode)
        {
            return new(listVisible || summaryVisible ? CardPanelTransition.HideBoth : CardPanelTransition.None, null);
        }

        if (selectedCardId is not { } selected)
        {
            var leaveAsIs = !summaryVisible && (listVisible || isAppearing);
            return new(leaveAsIs ? CardPanelTransition.None : CardPanelTransition.ShowList, null);
        }

        var sameCard = selected == summaryCardId;
        if (listVisible && listOpenedByUser && sameCard)
            return new(CardPanelTransition.None, selected);

        if (summaryVisible && !listVisible)
            return new(sameCard ? CardPanelTransition.None : CardPanelTransition.SwapSummaryCard, selected);

        return new(CardPanelTransition.CollapseToSummary, selected);
    }
}
