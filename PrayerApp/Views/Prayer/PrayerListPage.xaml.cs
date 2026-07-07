using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using PrayerApp.Helpers;
using PrayerApp.ViewModels;

namespace PrayerApp.Views.Prayer;

public partial class PrayerListPage : ContentPage
{
	public PrayerListPage(PrayerListViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (BindingContext is PrayerListViewModel vm)
			await PageSync.OnAppearingAsync(vm);
	}

	// Overflow toolbar handler — opens PrayersOverflowPopup (Pray, Add, Show Confidential).
	// Mirrors PrayerCardsPage.xaml.cs:291-305, simpler: the Prayers page has no
	// multi-select mode, so no fast-path branch is needed.
	private async void OnOverflowTapped(object? sender, EventArgs e)
	{
		if (BindingContext is not PrayerListViewModel vm) return;

		// Shape = null disables CT.Maui v14's default white-filled RoundRectangle
		// frame — our inner Border owns the rounded themed surface instead.
		var options = new PopupOptions { Shape = null };
		await this.ShowPopupAsync(new PrayersOverflowPopup(vm), options, CancellationToken.None);
	}

	private void OnSearchButtonPressed(object? sender, EventArgs e)
	{
		searchBar.Unfocus();
	}

	private void OnBackgroundTapped(object? sender, TappedEventArgs e)
	{
		if (searchBar.IsFocused)
			searchBar.Unfocus();
	}
}
