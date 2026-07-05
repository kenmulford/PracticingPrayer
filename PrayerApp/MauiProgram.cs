using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.Messaging;
using Maui.Biometric;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using PrayerApp.Messages;
using PrayerApp.Models;
using Plugin.LocalNotification;
using PrayerApp.Services;
using PrayerApp.Services.Confidential;
using PrayerApp.Shared;
using PrayerApp.Helpers;
using PrayerApp.ViewModels;
using PrayerApp.Views;
using PrayerApp.Views.Prayer;
using PrayerApp.Views.PrayerCard;
using PrayerApp.Views.PrayerTime;
using PrayerApp.Views.Settings;
using PrayerApp.Views.Tags;
using System.Globalization;
#if IOS
using Microsoft.Maui.Platform;
#endif

namespace PrayerApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Force English locale for all formatting (dates, AM/PM, numbers)
            var culture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            SQLitePCL.Batteries_V2.Init();

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "prayer_app.db");

            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    // Tap-the-active-tab pops that tab's nav stack to root.
                    // Shell's default no-ops (dotnet/maui#15301); custom renderer
                    // overrides OnTabReselected. See CustomShellRenderer.cs.
                    handlers.AddHandler(typeof(Shell),
                        typeof(Platforms.Android.CustomShellRenderer));
#endif
                });

            builder.UseMauiCommunityToolkit();
            // Issue #251: registers IBiometricAuthentication (Oscore.Maui.Biometric v2.5.1).
            builder.UseBiometricAuthentication();

            builder.UseLocalNotification(config =>
            {
#if ANDROID
                config.AddAndroid(android =>
                {
                    android.AddChannel(new Plugin.LocalNotification.Core.Models.AndroidOption.AndroidNotificationChannelRequest
                    {
                        Id = LocalNotificationCenterWrapper.PrayerRemindersChannelId,
                        Name = "Prayer Reminders",
                        Description = "Scheduled prayer reminder notifications",
                        Importance = Plugin.LocalNotification.Core.Models.AndroidOption.AndroidImportance.High,
                        EnableSound = true,
                        EnableVibration = true
                    });
                });
#endif
            });

            // F-10: Deep link handling via platform lifecycle events
            builder.ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                events.AddAndroid(android =>
                {
                    // Cold launch — Intent arrives with the activity
                    android.OnCreate((activity, _) =>
                        HandleAndroidIntent(activity.Intent));

                    // Warm launch — app already running, new link tapped
                    android.OnNewIntent((activity, intent) =>
                        HandleAndroidIntent(intent));

                    // Issue #257 — background privacy screen. OnPause fires before the OS
                    // captures the recents/app-switcher thumbnail, so the branded overlay is
                    // added here — not on the cross-platform Window.Deactivated event, which
                    // can fire too late relative to the snapshot. FLAG_SECURE itself is kept
                    // current by RefreshConfidentialCardsCacheAsync (on resume + on card/box
                    // change messages), so OnPause only needs to check it's already set.
                    android.OnPause(activity => Platforms.Android.PrivacyScreenOverlay.Show(activity));
                    android.OnResume(activity =>
                    {
                        Platforms.Android.PrivacyScreenOverlay.Hide(activity);
                        RefreshConfidentialCardsCacheAsync().SafeFireAndForget();
                    });
                });
#elif IOS
                events.AddiOS(ios =>
                {
                    // Warm launch via Universal Link (app already running)
                    ios.ContinueUserActivity((app, activity, handler) =>
                    {
                        if (activity.ActivityType == Foundation.NSUserActivityType.BrowsingWeb)
                            HandleDeepLink(activity.WebPageUrl?.ToString());
                        return true;
                    });

                    // Scene-based launch (iPadOS multi-window, cold + warm)
                    ios.SceneWillConnect((scene, session, options) =>
                    {
                        var activity = options.UserActivities?
                            .ToArray<Foundation.NSUserActivity>()
                            .FirstOrDefault(a =>
                                a.ActivityType == Foundation.NSUserActivityType.BrowsingWeb);
                        HandleDeepLink(activity?.WebPageUrl?.ToString());
                    });

                    // File open handler (.prayercard files) + share-extension wakeup.
                    ios.OpenUrl((app, url, options) =>
                    {
                        // Wakeup signal from our iOS Share Extension. The payload
                        // is already in the App Group container; AppGroupImportDispatcher
                        // (Window.Activated) picks it up on activation. Acknowledge the
                        // URL so iOS routes the launch to us instead of falling through.
                        if (url.Scheme == AppGroupConstants.HostAppScheme)
                            return true;

                        if (url.Path?.EndsWith(".prayercard", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            HandleFileOpen(url.Path);
                            return true;
                        }
                        return false;
                    });

                    // Issue #257 — background privacy screen. OnResignActivation (WillResignActive)
                    // fires synchronously before iOS captures the app-switcher snapshot, so the
                    // overlay must be added here — not on the cross-platform Window.Deactivated
                    // event, which is not guaranteed to run before the snapshot. The gate check
                    // is a cached flag (refreshed on OnActivated) since the overlay must be added
                    // synchronously and HasConfidentialCardsAsync is an async DB-backed call.
                    ios.OnResignActivation(app =>
                    {
                        if (_hasConfidentialCardsCache)
                            Platforms.iOS.PrivacyScreenOverlay.Show();
                    });
                    ios.OnActivated(app =>
                    {
                        Platforms.iOS.PrivacyScreenOverlay.Hide();
                        RefreshConfidentialCardsCacheAsync().SafeFireAndForget();
                    });
                });
#endif
            });

#if ANDROID
            PrayerApp.Platforms.Android.Handlers.TextInputTimePickerHandler.Configure();
#elif IOS
            PrayerApp.Platforms.iOS.Handlers.EnglishLocaleTimePickerHandler.Configure();
#endif

            // Fix Switch thumb color not syncing on initial load when IsToggled is already true.
            // The VisualStateManager "On" state doesn't fire until user interaction.
            Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("SyncInitialThumbColor", (handler, view) =>
            {
                if (view is Switch sw && sw.IsToggled)
                {
                    // Force the On VisualState so thumb color matches the style
                    VisualStateManager.GoToState(sw, "On");

#if IOS
                    // iOS dark-mode thumb-bug second-line defense (#52). The base ThumbColor
                    // setter in Styles.xaml ensures Switch.ThumbColor is non-null at handler-init,
                    // and MAUI's MapThumbColor synchronously pushes that through to
                    // uiSwitch.ThumbTintColor. But per MAUI's own SwitchHandler.iOS.cs SwitchProxy
                    // comment, "UIKit may re-apply default styles to internal views during certain
                    // lifecycle events" — empirically including first paint in dark mode. The
                    // synchronous set is clobbered; the deferred set survives. Mirrors MAUI's
                    // own DispatchAsync + Task.Delay(10) pattern from UpdateTrackOffColor (which
                    // patches the analogous OFF-track-color clobber).
                    // Source: github.com/dotnet/maui/blob/43db9d77/src/Core/src/Handlers/Switch/SwitchHandler.iOS.cs
                    var uiSwitch = handler.PlatformView as UIKit.UISwitch;
                    var thumbTint = sw.ThumbColor?.ToPlatform();
                    if (uiSwitch is not null && thumbTint is not null)
                    {
                        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(async () =>
                        {
                            await Task.Delay(10);
                            uiSwitch.ThumbTintColor = thumbTint;
                        });
                    }
#endif
                }
            });

#if IOS || MACCATALYST
            // Strip the native UITextField rounded border MauiPicker inherits by default.
            // Pickers are wrapped in our shared StyledPicker chrome; the nested OS-native
            // border was an unintended double-chrome artifact, exposed when StyledPicker's
            // outer chrome was removed in #35. Matches our DatePicker/TimePicker styling
            // which already renders flush against parents.
            Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("FlatPickerNoBorder", (handler, view) =>
            {
                handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // Cross-cutting messenger for entity-change signals (CommunityToolkit.Mvvm).
            // Services publish *ChangedMessage / BulkChangedMessage; ViewModels subscribe.
            builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

            // Add DB to scope as singleton; only need one connection for the life of the app.
            builder.Services.AddSingleton<IDBService>(s => new DBService(dbPath));
            // Register card service as singleton
            builder.Services.AddSingleton<ICardService, CardService>();
            // Register tag service as singleton
            builder.Services.AddSingleton<ITagService, TagService>();
            // Register prayer service as singleton
            builder.Services.AddSingleton<IPrayerService, PrayerService>();
            // Register prayer interaction service as singleton
            builder.Services.AddSingleton<IPrayerInteractionService, PrayerInteractionService>();
            // Register box service as singleton
            builder.Services.AddSingleton<IBoxService, BoxService>();
            // Singleton prayer-selection hand-off. Must be a single shared instance:
            // the producer (Set) and the consumer (Consume) run on opposite sides of a
            // navigation hop, so one instance has to hold the set across that hop. A
            // transient would hand each consumer a fresh, empty instance and Consume
            // would always return nothing. The data inside is single-use (Consume clears).
            builder.Services.AddSingleton<IPrayerSelectionService, PrayerSelectionService>();
            // Register local notification center wrapper (wraps Plugin.LocalNotification static)
            builder.Services.AddSingleton<ILocalNotificationCenter, LocalNotificationCenterWrapper>();
            // Register notification service — Settings.AllowNotifications supplied here so
            // NotificationService.cs itself has no MAUI dependency and can be unit-tested.
            builder.Services.AddSingleton<INotificationService>(sp =>
                new NotificationService(
                    sp.GetRequiredService<ILocalNotificationCenter>(),
                    () => PrayerApp.Services.Settings.AllowNotifications));
            // Register onboarding service as singleton
            builder.Services.AddSingleton<IOnboardingService, OnboardingService>();
            // Register diagnostic log service
            builder.Services.AddSingleton<IDiagnosticLog>(s => new DiagnosticLog(FileSystem.AppDataDirectory));
            // Register backup service
            builder.Services.AddSingleton<IBackupService, BackupService>();
            // Register user color service
            builder.Services.AddSingleton<IUserColorService, UserColorService>();
            // Register settings wrapper (delegates to static Settings, enables VM testing)
            builder.Services.AddSingleton<ISettings, SettingsService>();
            // Navigation + accessibility abstractions (enable VM unit testing)
            builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
            builder.Services.AddSingleton<IAccessibilityService, MauiAccessibilityService>();
            // OS share sheet abstraction (enables unit testing of share logic)
            builder.Services.AddSingleton<IShareService, ShareService>();
            // Confidential Cards (#251): biometric + PIN-fallback auth primitive.
            // Seam implementations wrap the Oscore biometric plugin, SecureStorage, and the
            // PIN-entry popup so ConfidentialAccessService's own logic stays unit-testable.
            builder.Services.AddSingleton<IBiometricAuthenticator, OscoreBiometricAuthenticator>();
            builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
            builder.Services.AddSingleton<IPinPrompt, MauiPinPrompt>();
            builder.Services.AddSingleton<IConfidentialAccessService, ConfidentialAccessService>();
            // Confidential-aware backup export (#256): passphrase-entry popup seam, mirroring
            // IPinPrompt's separation so BackupService.ExportAsync's branching stays testable.
            builder.Services.AddSingleton<IPassphrasePrompt, MauiPassphrasePrompt>();
            // Deep link sharing service.
            // Inbound deep-link / .prayercard imports stage a structured
            // payload and push ConfirmImportPage modally via
            // INavigationService.PushModalOnUiThreadAsync (which handles
            // the cold-start gate + UI-thread hop internally).
            builder.Services.AddSingleton<IDeepLinkService>(sp => new DeepLinkService(
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IShareService>(),
                sp.GetRequiredService<IImportPayloadService>(),
                () => sp.GetRequiredService<PrayerApp.Views.ConfirmImportPage>()));
            // Context-menu / share-extension import pipeline
            builder.Services.AddSingleton<IImportPayloadService, ImportPayloadService>();
            builder.Services.AddSingleton<ITextSelectionParser, TextSelectionParser>();

#if ANDROID
            builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.Android.OrientationService>();
            builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.Android.ColorPickerService>();
#elif IOS
            builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.iOS.OrientationService>();
            builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.iOS.ColorPickerService>();
            // App Group import orchestrator — reads pending-import.json staged
            // by the iOS Share Extension on every Window.Activated.
            builder.Services.AddSingleton<IAppGroupContainerProvider, PrayerApp.Platforms.iOS.NsFileManagerAppGroupContainerProvider>();
            builder.Services.AddSingleton(sp => new AppGroupImportOrchestrator(
                sp.GetRequiredService<IAppGroupContainerProvider>(),
                sp.GetRequiredService<IImportPayloadService>(),
                sp.GetRequiredService<INavigationService>(),
                () => sp.GetRequiredService<PrayerApp.Views.ConfirmImportPage>()));
#endif

            // ViewModels — Transient (fresh per page navigation)
            builder.Services.AddTransient<HomeViewModel>();
            builder.Services.AddTransient<PrayerCardsViewModel>();
            builder.Services.AddTransient<PrayerCardViewModel>();
            builder.Services.AddTransient<PrayerListViewModel>();
            builder.Services.AddTransient<PrayerRequestDetailViewModel>();
            builder.Services.AddTransient<ConfirmImportViewModel>();
            builder.Services.AddTransient<PrayerTimeViewModel>();
            builder.Services.AddTransient<PrayerTimeScopeViewModel>();
            builder.Services.AddTransient<PrayerTimeBoxScopeViewModel>();
            builder.Services.AddTransient<PrayerTimeCardSelectViewModel>();
            builder.Services.AddTransient<TagsViewModel>();
            builder.Services.AddTransient<TagDetailViewModel>();
            builder.Services.AddTransient<BoxesViewModel>();
            builder.Services.AddTransient<BoxDetailViewModel>();

            // Pages — Transient (Shell resolves from DI on navigation)
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<PrayerCardsPage>();
            builder.Services.AddTransient<PrayerCardPage>();
            builder.Services.AddTransient<PrayerListPage>();
            builder.Services.AddTransient<PrayerDetailPage>();
            builder.Services.AddTransient<ConfirmImportPage>();
            builder.Services.AddTransient<PrayerTimePage>();
            builder.Services.AddTransient<PrayerTimeScopePage>();
            builder.Services.AddTransient<PrayerTimeBoxScopePage>();
            builder.Services.AddTransient<PrayerTimeCardSelectPage>();
            builder.Services.AddTransient<TagsPage>();
            builder.Services.AddTransient<TagDetailPage>();
            builder.Services.AddTransient<Views.Boxes.BoxesPage>();
            builder.Services.AddTransient<Views.Boxes.BoxDetailPage>();
            builder.Services.AddTransient<SettingsHubPage>();
            builder.Services.AddTransient<AppSettingsPage>();
            builder.Services.AddTransient<BackupPage>();
            builder.Services.AddTransient<AboutPage>();
            builder.Services.AddTransient<HelpPage>();
            builder.Services.AddTransient<HelpViewModel>();

            RegisterGlobalExceptionHandlers();

            var app = builder.Build();

            // Issue #257 — keep the background-privacy-screen gate current on every
            // card/box change while the app runs (see RefreshConfidentialCardsCacheAsync).
            WirePrivacyScreenGateRefresh(app.Services);

            PrayerApp.Services.Settings.ConfigureNotificationService(
                app.Services.GetRequiredService<INotificationService>());

            // Wire notification tap → confirmation → Prayer Time navigation
            var notificationCenter = app.Services.GetRequiredService<ILocalNotificationCenter>();
            var tagServiceForNotification = app.Services.GetRequiredService<ITagService>();
            notificationCenter.NotificationTapped += async (_, notificationId) =>
            {
                await App.InitTask; // Ensure DB is ready

                var systemTag = await tagServiceForNotification.GetSystemTagAsync(TagService.RecentlyNotifiedTagName);
                if (systemTag is null) return;

                // Must run on UI thread for dialog and navigation
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var confirmed = await Shell.Current.DisplayAlertAsync(
                        "Prayer Time",
                        "Would you like to pray for your recently notified prayer requests now?",
                        "Yes", "No");

                    if (confirmed)
                    {
                        await Shell.Current.GoToAsync(
                            $"{nameof(PrayerApp.Views.PrayerTime.PrayerTimePage)}?scope={Routes.ScopeTags}&tagIds={systemTag.Id}");
                    }
                });
            };

            // Set DB service for the necessary models (synchronous — just stores a reference).
            var myDBService = app.Services.GetRequiredService<IDBService>();
            PrayerCard.SetDBService(myDBService);
            PrayerTag.SetDBService(myDBService);
            PrayerCardTag.SetDBService(myDBService);
            Prayer.SetDBService(myDBService);
            PrayerInteraction.SetDBService(myDBService);
            CardBox.SetDBService(myDBService);

            // Kick off seeding asynchronously — no blocking on the startup thread.
            // DBService internally awaits its own schema init before any query runs,
            // so seeding will wait for tables to exist automatically.
            // Pages await App.InitTask before loading data.
            App.InitTask = SeedAsync(app.Services);

            return app;
        }

        /// <summary>
        /// Issue #257 — cached "does the DB have any confidential card" flag. Both platforms'
        /// snapshot-blanking hooks (iOS <c>OnResignActivation</c>, Android <c>OnPause</c>) must
        /// decide synchronously whether to show the privacy overlay, but the underlying check
        /// (<see cref="IBoxService.HasConfidentialCardsAsync"/>) is an async DB-backed call — so
        /// it's kept current here instead, refreshed on app resume/activate AND on every
        /// card/box change message while the app is foregrounded (covers the case where a card
        /// is protected and the app is backgrounded again without an intervening resume).
        /// </summary>
        private static bool _hasConfidentialCardsCache;

        private static async Task RefreshConfidentialCardsCacheAsync()
        {
            await App.InitTask;
            var boxService = IPlatformApplication.Current?.Services?.GetService<IBoxService>();
            if (boxService is null) return;
            _hasConfidentialCardsCache = await boxService.HasConfidentialCardsAsync();

#if ANDROID
            // FLAG_SECURE can be applied immediately (cheap, idempotent); the branded overlay
            // itself is only ever added at OnPause (Show), gated on this same flag being set.
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity?.Window is not null)
            {
                if (_hasConfidentialCardsCache)
                    activity.Window.AddFlags(global::Android.Views.WindowManagerFlags.Secure);
                else
                    activity.Window.ClearFlags(global::Android.Views.WindowManagerFlags.Secure);
            }
#endif
        }

        /// <summary>
        /// Held alive for the app's lifetime as the WeakReferenceMessenger recipient key for
        /// the #257 privacy-screen gate refresh — mirrors the ViewModel Register&lt;TRecipient,...&gt;
        /// pattern (e.g. HomeViewModel.cs), but there's no ViewModel instance at this app-level
        /// scope, so a small dedicated static object plays that role instead.
        /// </summary>
        private static readonly object _privacyScreenGateRecipient = new();

        /// <summary>
        /// Wires the #257 privacy-screen cache to refresh whenever cards or boxes change, so a
        /// card protected while the app is foregrounded is caught before the next backgrounding
        /// even without an intervening resume/activate. Called once after the DI container is
        /// built (services, including the messenger, must already be resolvable).
        /// </summary>
        private static void WirePrivacyScreenGateRefresh(IServiceProvider services)
        {
            var messenger = services.GetRequiredService<IMessenger>();
            messenger.Register<object, PrayerCardChangedMessage>(_privacyScreenGateRecipient, (_, _) => RefreshConfidentialCardsCacheAsync().SafeFireAndForget());
            messenger.Register<object, CardBoxChangedMessage>(_privacyScreenGateRecipient, (_, _) => RefreshConfidentialCardsCacheAsync().SafeFireAndForget());
            messenger.Register<object, BulkChangedMessage>(_privacyScreenGateRecipient, (_, _) => RefreshConfidentialCardsCacheAsync().SafeFireAndForget());
        }

        /// <summary>
        /// Registers global exception handlers that log to <see cref="IDiagnosticLog"/>
        /// with a console fallback (DI may not be built yet during startup).
        /// Called once from CreateMauiApp — shared by both iOS and Android.
        /// </summary>
        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var log = IPlatformApplication.Current?.Services?.GetService<IDiagnosticLog>();
                if (log != null && ex != null)
                    log.Log("UnhandledException", ex);
                else
                    Console.Error.WriteLine(ex?.ToString() ?? e.ExceptionObject?.ToString());
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                var log = IPlatformApplication.Current?.Services?.GetService<IDiagnosticLog>();
                if (log != null)
                    log.Log("UnobservedTaskException", e.Exception);
                else
                    Console.Error.WriteLine(e.Exception.ToString());
                e.SetObserved();
            };
        }

#if ANDROID
        private static void HandleAndroidIntent(Android.Content.Intent? intent)
        {
            if (intent == null) return;

            // PROCESS_TEXT is API 23+; project MinSdk is 21 so the analyzer needs the runtime guard.
            if (intent.Action == Android.Content.Intent.ActionProcessText
                && OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                // EXTRA_PROCESS_TEXT is a CharSequence; GetStringExtra returns null for a
                // SpannableString (Chrome / Gmail rich-text), silently dropping the share.
                // See AOSP Intent.java EXTRA_PROCESS_TEXT JavaDoc.
                var text = intent.GetCharSequenceExtra(Android.Content.Intent.ExtraProcessText)?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    HandleSelectionImport(text);
                return;
            }

            if (intent.Action != Android.Content.Intent.ActionView || intent.Data is null)
                return;

            var uri = intent.Data.ToString();

            // URL-based deep link
            if (uri?.StartsWith("https://practicingprayerapp.com/share") == true)
            {
                HandleDeepLink(uri);
                return;
            }

            // File-based import (.prayercard)
            var mimeType = intent.Type;
            if (mimeType == "application/x-prayercard" || IsPrayerCardFile(intent))
            {
                HandleFileImport(intent);
            }
        }

        private static bool IsPrayerCardFile(Android.Content.Intent intent)
        {
            try
            {
                var context = Android.App.Application.Context;
                using var cursor = context.ContentResolver?.Query(
                    intent.Data!, null, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    var nameIndex = cursor.GetColumnIndex(
                        Android.Provider.IOpenableColumns.DisplayName);
                    if (nameIndex >= 0)
                    {
                        var name = cursor.GetString(nameIndex);
                        return name?.EndsWith(".prayercard", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
            }
            catch { /* Ignore cursor errors */ }
            return false;
        }

        private static void HandleFileImport(Android.Content.Intent intent)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await App.InitTask;
                    var context = Android.App.Application.Context;
                    using var inputStream = context.ContentResolver?.OpenInputStream(intent.Data!);
                    if (inputStream == null) return;

                    var svc = IPlatformApplication.Current!.Services.GetRequiredService<IDeepLinkService>();
                    await svc.HandleFileAsync(inputStream);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DeepLink] File import failed: {ex.Message}");
                }
            });
        }

        private static void HandleSelectionImport(string text)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await App.InitTask;
                    var services = IPlatformApplication.Current!.Services;
                    services.GetRequiredService<IImportPayloadService>().StagePayload(text);
                    await services.GetRequiredService<INavigationService>()
                        .PushModalWithNavigationBarAsync(
                            services.GetRequiredService<ConfirmImportPage>());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ContextMenu] Selection import failed: {ex.Message}");
                }
            });
        }
#endif

        /// <summary>
        /// Processes an incoming Universal Link / App Link URI.
        /// Waits for DB initialization, then delegates to DeepLinkService.
        /// </summary>
        private static void HandleDeepLink(string? url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://practicingprayerapp.com/share"))
                return;

            // Strip trailing text — share messages append human-readable summary
            // after the URL, which some apps pass through as part of the URI.
            var endOfUrl = url.IndexOfAny(new[] { '\n', '\r', ' ' });
            if (endOfUrl >= 0)
                url = url[..endOfUrl];

            // Suppress onboarding for this session — must happen before UI dispatch
            // so MainPage.OnAppearing sees the flag when it checks.
            var onboarding = IPlatformApplication.Current?.Services?.GetService<IOnboardingService>();
            onboarding?.MarkDeepLinkSession();

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await App.InitTask;
                    var svc = IPlatformApplication.Current!.Services.GetRequiredService<IDeepLinkService>();
                    await svc.HandleAsync(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DeepLink] HandleDeepLink failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Processes a .prayercard file opened via the OS (iOS OpenUrl / Android file intent).
        /// </summary>
        private static void HandleFileOpen(string filePath)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await App.InitTask;
                    await using var stream = File.OpenRead(filePath);
                    var svc = IPlatformApplication.Current!.Services.GetRequiredService<IDeepLinkService>();
                    await svc.HandleFileAsync(stream);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DeepLink] File open failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Runs post-schema seed work asynchronously. Awaited by App.InitTask
        /// before the first page loads data.
        /// </summary>
        private static async Task SeedAsync(IServiceProvider services)
        {
            // Trim the diagnostic log on startup (non-blocking — runs inside InitTask)
            services.GetRequiredService<IDiagnosticLog>().Trim();

            var userColorService = services.GetRequiredService<IUserColorService>();
            await userColorService.SeedDefaultsAsync();

            var tagService = services.GetRequiredService<ITagService>();
            await tagService.SeedSystemTagsAsync();

            // Ensure system boxes (System, Archived) exist — resilience fallback
            var boxService = services.GetRequiredService<IBoxService>();
            await boxService.SeedSystemBoxesAsync();

            // Ensure ArchivedFolderId setting is in sync (covers edge cases where
            // DBService migration wrote it but the box was re-created by seed)
            var archivedBox = await boxService.GetSystemBoxAsync(CardBox.SystemKeyArchived);
            if (archivedBox != null)
                Settings.ArchivedFolderId = archivedBox.Id;

            // Ensure the system "Quick Add" card exists
            var cardService = services.GetRequiredService<ICardService>();
            await cardService.GetOrCreateQuickAddCardAsync();

            // BUG-58 safety net: fix ANY system cards still at BoxId=0 (legacy installs)
            var sysBox = await boxService.GetSystemBoxAsync(CardBox.SystemKeySystem);
            if (sysBox != null)
            {
                var allCards = await cardService.GetCardsAsync();
                foreach (var card in allCards.Where(c => c.IsSystem && c.BoxId == 0))
                    await cardService.AssignBoxAsync(card, sysBox.Id);
            }

            // Load active prayers once — reused for recently-notified tagging and M-11 renewal
            var prayerService = services.GetRequiredService<IPrayerService>();
            var activePrayers = await prayerService.GetAllActivePrayersAsync();

            // Tag prayers that were recently notified (within last 24h based on schedule)
            try
            {
                var recentIds = NotificationHelper.GetRecentlyNotifiedPrayerIds(activePrayers, DateTime.Now);

                var systemTag = await tagService.GetSystemTagAsync(TagService.RecentlyNotifiedTagName);
                if (systemTag is not null)
                {
                    await tagService.ClearAllAssignmentsForTagAsync(systemTag.Id);
                    foreach (var prayerId in recentIds)
                        await tagService.AddTagToRequestAsync(prayerId, systemTag.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Recently-notified tagging failed: {ex}");
            }

            // Reconcile notifications: clear orphans from deleted prayers or prior
            // app versions, then reschedule all prayers with CanNotify=true.
            // On Android this also renews the 12 monthly one-shot notifications (M-11).
            // On iOS this clears native UNCalendarNotificationTrigger orphans.
            try
            {
                var notificationService = services.GetRequiredService<INotificationService>();
                await notificationService.ReconcileNotificationsAsync(activePrayers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification reconciliation failed: {ex}");
            }

#if DEBUG
            if (PrayerApp.Services.Settings.FirstRun)
            {
                var dbService = services.GetRequiredService<IDBService>();
                await dbService.SeedDataAsync();
                PrayerApp.Services.Settings.FirstRun = false;
            }
#endif
        }
    }
}
