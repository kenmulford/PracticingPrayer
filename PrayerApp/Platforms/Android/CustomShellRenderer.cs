using Android.Content;
using Google.Android.Material.AppBar;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using AToolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace PrayerApp.Platforms.Android;

/// <summary>
/// Custom Shell renderer that makes tapping the already-active tab in the bottom
/// tab bar pop the tab's navigation stack back to its root. MAUI Shell's default
/// no-ops same-tab taps (dotnet/maui#15301); this overrides
/// <see cref="ShellItemRenderer.OnTabReselected"/> — invoked by Android's
/// <c>NavigationBarView.IOnItemSelectedListener</c> when the currently-selected
/// item is tapped again — to call <c>PopToRootAsync</c> on that tab's section.
///
/// Android-only. iOS Shell uses a different renderer stack (UITabBarController);
/// the equivalent there is out of scope for this change.
///
/// It also paints the Shell AppBarLayout (visible behind the transparent status
/// bar) from <c>colorShellAppBar</c>.
/// </summary>
public class CustomShellRenderer : ShellRenderer
{
    public CustomShellRenderer(Context context) : base(context) { }

    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem)
        => new PopToRootShellItemRenderer(this);

    protected override IShellToolbarAppearanceTracker CreateToolbarAppearanceTracker()
        => new AppBarColorToolbarAppearanceTracker(this);

    private sealed class AppBarColorToolbarAppearanceTracker : ShellToolbarAppearanceTracker
    {
        public AppBarColorToolbarAppearanceTracker(IShellContext shellContext) : base(shellContext) { }

        protected override void SetColors(AToolbar toolbar, IShellToolbarTracker toolbarTracker, Color foreground, Color background, Color title)
        {
            base.SetColors(toolbar, toolbarTracker, foreground, background, title);

            if (toolbar.Parent is AppBarLayout appBar)
                appBar.SetBackgroundResource(Resource.Color.colorShellAppBar);
        }
    }

    private sealed class PopToRootShellItemRenderer : ShellItemRenderer
    {
        public PopToRootShellItemRenderer(IShellContext shellContext) : base(shellContext) { }

        protected override void OnTabReselected(ShellSection shellSection)
        {
            base.OnTabReselected(shellSection);
            if (shellSection is null) return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await shellSection.Navigation.PopToRootAsync();
                }
                catch
                {
                    // Best-effort — navigation may be canceled (e.g. IEditGuard).
                }
            });
        }
    }
}
