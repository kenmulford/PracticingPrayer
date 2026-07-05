using CommunityToolkit.Maui.Views;
using PrayerApp.ViewModels;

namespace PrayerApp.Views.Prayer;

public partial class PrayersOverflowPopup : Popup
{
    private readonly PrayerListViewModel _vm;

    public PrayersOverflowPopup(PrayerListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    // Close-before-execute: mirrors CardsOverflowPopup.xaml.cs — navigation/auth commands
    // can fail if the modal popup is still owning the presentation stack.
    //
    // CanExecute guard (issue #298 regression fix): the Pray row's IsEnabled binding blocks
    // the TapGestureRecognizer in the normal case, but ICommand.Execute does not itself gate
    // on CanExecute — so guard explicitly here as belt-and-suspenders against any platform
    // quirk that lets a disabled Grid's tap through. Without this, tapping with 0 active
    // prayers in view silently no-ops inside StartPrayerTimeAsync's defensive early return.
    private async void OnPrayTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        if (_vm.StartPrayerTimeCommand.CanExecute(null))
            _vm.StartPrayerTimeCommand.Execute(null);
    }

    private async void OnAddTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync(CancellationToken.None);
        _vm.NewCommand.Execute(null);
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
