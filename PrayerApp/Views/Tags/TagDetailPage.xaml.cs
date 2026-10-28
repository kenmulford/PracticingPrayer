using PrayerApp.Helpers;
using PrayerApp.ViewModels;
#if ANDROID
using Android.Views;
#elif IOS
using UIKit;
#endif

namespace PrayerApp.Views.Tags
{
    public partial class TagDetailPage : ContentPage
    {
#if IOS
        // Track native gesture recognizers so we can remove them before the page
        // is torn down. Without cleanup, the UILongPressGestureRecognizer closure
        // holds a strong reference to the managed Grid → BindingContext chain.
        // When Shell pops this page, iOS deallocates native views while the GC
        // may collect the managed objects — that race causes SIGABRT. (BUG-1)
        private readonly List<(UIView NativeView, UIGestureRecognizer Recognizer)> _nativeGestures = new();
#elif ANDROID
        // Loaded re-fires on every re-attach; attach the Touch handler once per native view.
        private readonly NativeAttachGuard _swatchGestureGuard = new();
#endif

        public TagDetailPage(TagDetailViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
#if IOS
            Platforms.iOS.Helpers.SwipeBackHelper.DisableSwipeBack(this);
#endif
            if (BindingContext is TagDetailViewModel vm && !vm.IsExisting)
                Dispatcher.DispatchAsync(() => NameEntry.Focus());
        }

        protected override void OnDisappearing()
        {
#if IOS
            Platforms.iOS.Helpers.SwipeBackHelper.EnableSwipeBack(this);
#endif
            base.OnDisappearing();
#if IOS
            // Detach all native gesture recognizers before the page is popped.
            // This breaks the strong reference chain and prevents the SIGABRT
            // that occurs when iOS deallocates native views while managed objects
            // are being collected.
            foreach (var (nativeView, recognizer) in _nativeGestures)
                nativeView.RemoveGestureRecognizer(recognizer);
            _nativeGestures.Clear();
#endif
            // Signal the ViewModel to stop any in-flight async work (e.g.
            // LoadSwatchesAsync modifying the ObservableCollection after the
            // page's layout handlers have been torn down).
            if (BindingContext is TagDetailViewModel tdvm)
                tdvm.CancelPendingWork();
        }

        private void OnSwatchLoaded(object? sender, EventArgs e)
        {
            if (sender is not Grid grid) return;

            // Handler may not be set yet at Loaded time — hook HandlerChanged
            if (grid.Handler is not null)
                AttachNativeLongPress(grid);
            else
            {
                // -= first: a second Loaded before the handler exists must not double-subscribe.
                grid.HandlerChanged -= OnSwatchHandlerChanged;
                grid.HandlerChanged += OnSwatchHandlerChanged;
            }
        }

        private void OnSwatchHandlerChanged(object? sender, EventArgs e)
        {
            if (sender is Grid grid && grid.Handler is not null)
            {
                grid.HandlerChanged -= OnSwatchHandlerChanged;
                AttachNativeLongPress(grid);
            }
        }

        private void AttachNativeLongPress(Grid grid)
        {
#if ANDROID
            if (grid.Handler?.PlatformView is Android.Views.View nativeView
                && _swatchGestureGuard.TryMark(nativeView))
            {
                // Handle BOTH tap and long-press natively on Android.
                // MAUI's TapGestureRecognizer and the native GestureDetector
                // corrupt each other's touch pipelines when both are active.
                // By owning the full touch sequence natively (OnDown → true,
                // args.Handled = detector result), we avoid all conflicts.
                // The XAML TapGestureRecognizer remains for iOS where no
                // native handler is attached.
                var listener = new SwatchGestureListener(grid);
                var detector = new GestureDetector(nativeView.Context, listener);
                nativeView.Touch += (s, args) =>
                {
                    args.Handled = detector.OnTouchEvent(args.Event!);
                };
            }
#elif IOS
            if (grid.Handler?.PlatformView is UIView nativeView)
            {
                // Use a WeakReference to the grid so the gesture recognizer
                // closure doesn't prevent GC of the managed MAUI object.
                var weakGrid = new WeakReference<Grid>(grid);
                var longPress = new UILongPressGestureRecognizer(r =>
                {
                    if (r.State == UIGestureRecognizerState.Began &&
                        weakGrid.TryGetTarget(out var g) &&
                        g.BindingContext is ColorSwatchViewModel vm &&
                        vm.DeleteCommand.CanExecute(null))
                    {
                        MainThread.BeginInvokeOnMainThread(() => vm.DeleteCommand.Execute(null));
                    }
                });
                longPress.MinimumPressDuration = 0.5; // 500ms, matches prior LongPressDuration
                nativeView.AddGestureRecognizer(longPress);
                _nativeGestures.Add((nativeView, longPress));
            }
#endif
        }

#if ANDROID
        /// <summary>
        /// Handles both tap (select) and long-press (delete) natively on Android.
        /// MAUI's TapGestureRecognizer and native GestureDetector corrupt each
        /// other's touch pipelines, so we handle everything at the native level.
        /// </summary>
        private sealed class SwatchGestureListener : GestureDetector.SimpleOnGestureListener
        {
            private readonly Grid _grid;
            public SwatchGestureListener(Grid grid) => _grid = grid;

            public override bool OnDown(MotionEvent? e) => true;

            public override bool OnSingleTapUp(MotionEvent? e)
            {
                if (_grid.BindingContext is ColorSwatchViewModel vm)
                    vm.SelectCommand.Execute(null);
                return true;
            }

            public override void OnLongPress(MotionEvent? e)
            {
                if (_grid.BindingContext is ColorSwatchViewModel vm &&
                    vm.DeleteCommand.CanExecute(null))
                {
                    MainThread.BeginInvokeOnMainThread(() => vm.DeleteCommand.Execute(null));
                }
            }
        }
#endif

        private void OnBackgroundTapped(object? sender, TappedEventArgs e)
        {
            if (NameEntry.IsFocused)
                NameEntry.Unfocus();
        }
    }
}
