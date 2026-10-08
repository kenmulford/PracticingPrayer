---
name: prayer-app-platform
description: Use when working on Android/iOS platform-specific code in PrayerApp — MainActivity IntentFilters, CustomShellRenderer, PageSheetPresentation, handler Configure() registration, ColorPickerService, deep linking, file import (.prayercard), Universal Links, Entitlements, PrivacyInfo, conditional compilation, orientation, or build config.
---

# PrayerApp Platform-Specific Code

Platform code lives under `PrayerApp/Platforms/Android/` and `PrayerApp/Platforms/iOS/`.

---

## When to Use

- Adding or changing an Android `IntentFilter` (deep link or file import)
- Registering a new handler or patching an existing one
- Adding a platform service (orientation, color picker, etc.)
- Working with iOS Universal Links, Entitlements, or PrivacyInfo
- Adding `#if ANDROID` / `#if IOS` conditional compilation
- Touching build config, signing, or iOS code-signing entitlements

---

## Quick Reference — Files by Platform

| File | Purpose |
|------|---------|
| `Platforms/Android/MainActivity.cs` | Activity attributes, two `IntentFilter`s (deep link + file import) |
| `Platforms/Android/CustomShellRenderer.cs` | `OnTabReselected` → pop-to-root workaround (dotnet/maui#15301) |
| `Platforms/Android/OrientationService.cs` | Lock/unlock portrait/landscape via `Platform.CurrentActivity` |
| `Platforms/Android/ColorPickerService.cs` | `IColorPickerService` — shows `ColorPickerPopup` via CT.Maui |
| `Platforms/Android/Handlers/TextInputTimePickerHandler.cs` | Forces Material TimePicker to keyboard/text input mode |
| `Platforms/Android/AndroidManifest.xml` | Permissions (notifications, network, boot, alarms, vibrate) |
| `Platforms/Android/Resources/values/colors.xml` | Material color tokens mapped from MAUI theme |
| `Platforms/iOS/AppDelegate.cs` | Orientation delegate (`GetSupportedInterfaceOrientations`) |
| `Platforms/iOS/OrientationService.cs` | `UIWindowSceneGeometryPreferencesIOS` orientation update |
| `Platforms/iOS/ColorPickerService.cs` | `IColorPickerService` — wraps `NativeColorPicker` |
| `Platforms/iOS/Handlers/EnglishLocaleTimePickerHandler.cs` | Forces `UIDatePicker` locale to `en_US` |
| `Platforms/iOS/Helpers/SwipeBackHelper.cs` | Disable/enable iOS swipe-back gesture on edit pages |
| `Platforms/iOS/Info.plist` | Device families, orientations, UTI declarations, bundle version |
| `Platforms/iOS/Entitlements.plist` | `com.apple.developer.associated-domains` for Universal Links |
| `Platforms/iOS/Resources/PrivacyInfo.xcprivacy` | Apple Privacy Manifest (required since iOS 17.4) |
| `Platforms/iOS/LinkerConfig.xml` | Trim preservation for SQLite-net AOT compatibility |
| `MauiProgram.cs` | Handler registration, lifecycle hooks, platform service DI |
| `PrayerApp.csproj` | Target frameworks, signing, `CodesignEntitlements` (both configs) |

---

## Android — MainActivity

Two `[IntentFilter]` attributes on `MainActivity`:

```csharp
// 1. HTTPS deep links — https://practicingprayerapp.com/share/*
[IntentFilter(
    new[] { Android.Content.Intent.ActionView },
    Categories = new[] { CategoryDefault, CategoryBrowsable },
    DataScheme = "https",
    DataHost = "practicingprayerapp.com",
    DataPathPrefix = "/share",
    AutoVerify = true)]

// 2. .prayercard file import via content:// URIs
[IntentFilter(
    new[] { Android.Content.Intent.ActionView },
    Categories = new[] { CategoryDefault, CategoryBrowsable },
    DataScheme = "content",
    DataMimeType = "application/x-prayercard")]
```

Both intents are dispatched through `HandleAndroidIntent` in `MauiProgram.cs`. URL deep links call `HandleDeepLink`; file intents call `HandleFileImport` (uses `ContentResolver` to open an `InputStream`).

---

## Android — CustomShellRenderer

`Platforms/Android/CustomShellRenderer.cs` — registered in `ConfigureMauiHandlers` under `#if ANDROID`:

```csharp
handlers.AddHandler(typeof(Shell), typeof(Platforms.Android.CustomShellRenderer));
```

Overrides `OnTabReselected` → calls `shellSection.Navigation.PopToRootAsync()`. Workaround for dotnet/maui#15301 where tapping the active tab is a no-op by default.

---

## Handler Registration Pattern

Handlers use static `.Configure()` calls placed **outside** `ConfigureMauiHandlers`, not `handlers.AddHandler<>()`:

```csharp
// In MauiProgram.CreateMauiApp(), after builder.UseMauiCommunityToolkit():
#if ANDROID
PrayerApp.Platforms.Android.Handlers.TextInputTimePickerHandler.Configure();
#elif IOS
PrayerApp.Platforms.iOS.Handlers.EnglishLocaleTimePickerHandler.Configure();
#endif
```

The global `SwitchHandler` thumb-color patch uses `AppendToMapping` directly on the mapper (no `#if`):

```csharp
Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("SyncInitialThumbColor", (handler, view) =>
{
    if (view is Switch sw && sw.IsToggled)
        VisualStateManager.GoToState(sw, "On");
});
```

---

## iOS — PageSheetPresentation

`Views/PageSheetPresentation.cs` — on `Application.ModalPushing` it sets `UIModalPresentationStyle.PageSheet` for a page that implements `IPageSheetModal` or a `NavigationPage` whose `RootPage` implements it. Marked pages are page sheets on iPhone and iPad. Pages that should stay full-screen (e.g. `RestoreProgressPage`) do not implement the interface.

---

## iOS — Lifecycle Hooks (MauiProgram.cs)

Three lifecycle hooks registered under `#elif IOS`:

1. `ContinueUserActivity` — warm Universal Link launch
2. `SceneWillConnect` — cold/warm scene-based launch (iPad multi-window)
3. `OpenUrl` — `.prayercard` file opens (path ends with `.prayercard` → `HandleFileOpen`)

---

## iOS — Platform Files

**`Entitlements.plist`** (`Platforms/iOS/Entitlements.plist`)
- `com.apple.developer.associated-domains`: `applinks:practicingprayerapp.com`
- Required for Universal Links; referenced via `CodesignEntitlements` in **both** Debug and Release `PropertyGroup`s in the csproj.

**`PrivacyInfo.xcprivacy`** (`Platforms/iOS/Resources/PrivacyInfo.xcprivacy`)
- Apple Privacy Manifest required since iOS 17.4 for App Store submission.
- Declares accessed API categories: FileTimestamp, SystemBootTime, DiskSpace, UserDefaults.
- `NSPrivacyTracking = false`, no collected data types.

**`Info.plist`**
- Bundle version: `1.2.5` (build 64)
- Document type declared for `.prayercard` UTI (`com.multithreadedllc.prayercards.prayercard`)
- `LSSupportsOpeningDocumentsInPlace = false`

---

## Platform Service DI Registration

```csharp
#if ANDROID
builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.Android.OrientationService>();
builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.Android.ColorPickerService>();
#elif IOS
builder.Services.AddSingleton<IOrientationService, PrayerApp.Platforms.iOS.OrientationService>();
builder.Services.AddSingleton<IColorPickerService, PrayerApp.Platforms.iOS.ColorPickerService>();
#endif
```

---

## Conditional Compilation Locations

| File | Directives | Purpose |
|------|-----------|---------|
| `MauiProgram.cs` | `#if ANDROID` / `#elif IOS` (5+) | Shell renderer, lifecycle hooks, handler config, service registration |
| `Views/PrayerCard/PrayerCardsPage.xaml.cs` | `#if ANDROID` (3) | Platform-specific card layout behavior |
| `Views/Boxes/BoxDetailPage.xaml.cs` | `#if IOS` (2) | iOS swipe-back control |
| `Views/Tags/TagDetailPage.xaml.cs` | `#if ANDROID` (3), `#if IOS` (4) | Platform-specific tag editing behavior |
| `Helpers/Diagnostics.cs` | `#if ANDROID \|\| IOS` (1) | Platform-aware diagnostic calls |
| `Services/LocalNotificationCenterWrapper.cs` | `#if ANDROID` / `#if IOS` (4) | Notification API differences |

---

## Build Configuration Highlights

- `ApplicationDisplayVersion`: `1.2.5`, `ApplicationVersion`: `64`
- iOS minimum: 16.0; Android minimum: API 24
- `CodesignEntitlements` is set in **both** Debug and Release iOS PropertyGroups — required for Universal Links in both configurations
- Android signing via env vars `ANDROID_SIGNING_STORE_PASS` / `ANDROID_SIGNING_KEY_PASS`; build succeeds debug-signed when absent

---

## Common Mistakes

| Mistake | Correct Approach |
|---------|-----------------|
| Using `handlers.AddHandler<TimePicker, MyHandler>()` for the time/date picker handlers | Use the static `.Configure()` pattern; handlers use `AppendToMapping` internally |
| Forgetting the second `IntentFilter` for `.prayercard` file import | `MainActivity.cs` has two filters — one for HTTPS deep links, one for `content://` file URIs |
| Missing `CodesignEntitlements` in Debug configuration | Both Debug and Release PropertyGroups in the csproj must reference `Entitlements.plist` — Universal Links require it even in debug builds |
| Omitting `PrivacyInfo.xcprivacy` | Required since iOS 17.4; App Store will reject submissions without it |
| Implementing tab-tap pop-to-root in Shell XAML or `AppShell.xaml.cs` | The fix lives in `CustomShellRenderer` (Android-only); iOS uses a different renderer stack |
| Hardcoding `UIModalPresentationStyle.PageSheet` on all modals | Only pages implementing `IPageSheetModal` get PageSheet; blocking/progress pages must stay full-screen |
