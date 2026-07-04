using CommunityToolkit.Maui.Views;
using PrayerApp.ViewModels;

namespace PrayerApp.Views.PrayerCard;

public partial class CardsOverflowPopup : Popup
{
    private readonly PrayerCardsViewModel _vm;

    public CardsOverflowPopup(PrayerCardsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    // Close-before-execute: navigation commands (NewCommand, OpenCollectionsCommand)
    // can fail if the modal popup is still owning the presentation stack.
    private async void OnAddCardTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        _vm.NewCommand.Execute(null);
    }

    private async void OnCollectionsTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        _vm.OpenCollectionsCommand.Execute(null);
    }

    private async void OnSelectTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        _vm.EnterMultiSelectCommand.Execute(null);
    }

    // Close-before-execute (see class summary above): ShowConfidentialCommand drives
    // a biometric/PIN prompt (IConfidentialAccessService.AuthenticateAsync), which
    // should not compete with the popup for the presentation stack.
    private async void OnShowConfidentialTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        _vm.ShowConfidentialCommand.Execute(null);
    }
}
