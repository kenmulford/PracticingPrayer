---
name: prayer-app-theming
description: Use when applying colors, named styles, AppThemeBinding, value converters, or dark/light mode in PrayerApp — color token reference, style inheritance rules, Shell theming, tag palette, and gotchas specific to this codebase.
---

# PrayerApp Theming & Styling

Theme files: `PrayerApp/Resources/Styles/Colors.xaml` and `Styles.xaml`

---

## When to Use

- Adding or changing any color, style, or dark/light mode behavior
- Styling a new view, card, or form field
- Referencing a named style (`PrayerCardBorder`, `DividerLine`, etc.)
- Wiring up a value converter
- Diagnosing a style not propagating in a `BindableLayout`

---

## Design Aesthetic

- **Warm journal** — muted olive-green primary, warm paper backgrounds
- **No harsh contrasts** — softer dark mode (not pure black/white)
- **WarmGold accents** for important CTAs (Answered, Prayer Time)
- Fonts: OpenSans Regular/Semibold only

---

## Quick Reference — Most-Used Tokens & Styles

| Token / Style | Light | Dark | Notes |
|---|---|---|---|
| `PageLight` / `PageDark` | `#FAF8F3` | `#0d0e0c` | Page background |
| `CardLight` / `CardDark` | `#FFFFFF` | `#181a16` | Card bg; also set by `PrayerCardBorder` |
| `CardExpandedLight` / `CardExpandedDark` | `#FFFAF0` | `#2A2820` | "Lifted paper" tint for expanded cards |
| `Primary` / `PrimaryDark` | `#6B7D5A` | `#A8B896` | Brand color |
| `WarmGold` / `WarmGoldDark` | `#C4A052` | `#d4b062` | Answered, Prayer Time CTAs |
| `LoadingScrimLight` / `LoadingScrimDark` | `#66000000` | `#A6000000` | Translucent backdrop behind activity indicators |
| `TagText` | `White` | `White` | All tag chip foreground — same in both modes |
| `PrayerCardBorder` | Border style | Sets `BackgroundColor` (CardLight/CardDark), 8px corners, no stroke in `Normal`; `CardStates` VSM: `Highlighted` = WarmGold/WarmGoldDark 2.5px, `MultiSelected` = Primary/PrimaryDark 2.5px |
| `DividerLine` | BoxView style | 1px rule; `Opacity` is `{AppThemeBinding Light=0.4, Dark=0.85}` — NOT static |
| `SaveActivityIndicator` | Named style | Centered ActivityIndicator; Primary (light) / PrimaryBtnDark (dark) |
| `PickerIndicator` | Label style | Decorative ▼ arrow; `BasedOn="LabelBase"` |

---

## Complete Color Reference

### Brand & Accent
| Token | Hex | Usage |
|---|---|---|
| `Primary` | `#6B7D5A` | Main brand, buttons, borders (light) |
| `PrimaryDark` | `#A8B896` | Brand in dark mode |
| `PrimaryDarkText` | `#1C2415` | Text on primary background |
| `Secondary` | `#E8EDE3` | Pale beige secondary |
| `SecondaryDarkText` | `#7A8C68` | Secondary text dark |
| `Tertiary` | `#3F4A34` | Deep forest green |
| `WarmGold` | `#C4A052` | Important CTAs, answered state |
| `WarmGoldDark` | `#d4b062` | WarmGold dark variant |
| `DeepEarth` | `#2D3428` | Deep accent |
| `OffBlack` | `#1f1f1f` | Near-black text |

### Surface Colors
| Token | Hex | Usage |
|---|---|---|
| `PageLight` | `#FAF8F3` | Page background (warm paper) |
| `PageDark` | `#0d0e0c` | Page background dark |
| `CardLight` | `#FFFFFF` | Card background light |
| `CardDark` | `#181a16` | Card background dark |
| `CardExpandedLight` | `#FFFAF0` | Expanded card "lifted paper" tint (light) |
| `CardExpandedDark` | `#2A2820` | Expanded card tint (dark) |
| `SurfaceDark` | `#1c1e19` | Elevated surface dark |
| `TabBarDark` | `#0a0b09` | Tab bar dark |
| `CardBorderDark` | `#2a2e26` | Card border dark |
| `SystemCardLight` | `#F0F3EC` | System/info card light |
| `SystemCardDark` | `#1e201b` | System/info card dark |
| `LoadingScrimLight` | `#66000000` | Translucent loading backdrop (light) |
| `LoadingScrimDark` | `#A6000000` | Translucent loading backdrop (dark) |

### Text Colors
| Token | Hex | Usage |
|---|---|---|
| `TextPrimaryDark` | `#dce0d5` | Primary text dark mode |
| `TextSecondaryDark` | `#9a9e92` | Secondary text dark |
| `TextMutedDark` | `#858981` | Muted/disabled dark |
| `TabUnselectedDark` | `#4a4d46` | Unselected tab dark |

### Button Colors
| Token | Hex | Usage |
|---|---|---|
| `ButtonSecondaryLight` | `#E8EDE3` | Secondary button light |
| `ButtonSecondaryDark` | `#252722` | Secondary button dark |
| `PrimaryBtnDark` | `#7d8e6c` | Primary button dark |

### Status Colors
| Token | Hex | Usage |
|---|---|---|
| `SuccessGreen` / `SuccessGreenDark` | `#2E7D32` / `#66BB6A` | Success states |
| `DangerRed` / `DangerRedDark` | `#CD5C5C` / `#E07070` | Delete, danger |
| `OverdueLight` / `OverdueDark` | `#FFF8E7` / `#1a1810` | Overdue card tint |

### Chip Colors (Light/Dark pairs)
| Purpose | Light | Dark |
|---|---|---|
| `ChipBgLight` / `ChipBgDark` | `#F0F0F0` | `#2A2E26` |
| `ChipFavoritedBgLight` / `ChipFavoritedBgDark` | `#F5EDD4` | `#3A3420` |
| `ChipDangerBgLight` / `ChipDangerBgDark` | `#FDE8E8` | `#3A1B1B` |

### Tag Color Palette (8 hues — all use `TagText` = White)
| Name | Light Hex | Dark Hex |
|---|---|---|
| `TagRed` / `TagRedDark` | `#B84040` | `#D46060` |
| `TagOrange` / `TagOrangeDark` | `#B35A20` | `#CC7040` |
| `TagBrown` / `TagBrownDark` | `#7A4020` | `#A65C34` |
| `TagTeal` / `TagTealDark` | `#1E7870` | `#3AA898` |
| `TagBlue` / `TagBlueDark` | `#2E5A9A` | `#507ACC` |
| `TagPurple` / `TagPurpleDark` | `#663C8C` | `#9460C0` |
| `TagPink` / `TagPinkDark` | `#8C3860` | `#B85A8C` |
| `TagGray` / `TagGrayDark` | `#505050` | `#848484` |

Tag foreground: always `{StaticResource TagText}` (White in both modes).

### Gray Scale
`Gray100` (`#E1E1E1`) → `Gray200` → `Gray300` → `Gray400` → `Gray500` (`#6E6E6E`) → `Gray600` → `Gray700` → `Gray800` → `Gray900` (`#212121`) → `Gray950` (`#141414`)

---

## AppThemeBinding Syntax

`AppThemeBinding` works on **any scalar property**, not only colors — `Opacity`, strings, numbers.

```xml
<!-- Color property -->
<Label TextColor="{AppThemeBinding Light={StaticResource Primary},
                                    Dark={StaticResource PrimaryDark}}" />

<!-- Scalar (Opacity) — same syntax works -->
<BoxView Opacity="{AppThemeBinding Light=0.4, Dark=0.85}" />

<!-- Inside a Style Setter -->
<Setter Property="BackgroundColor"
        Value="{AppThemeBinding Light={StaticResource PageLight},
                                Dark={StaticResource PageDark}}" />

<!-- Inside a DataTrigger -->
<DataTrigger TargetType="Button" Binding="{Binding IsSelected}" Value="True">
    <Setter Property="BackgroundColor"
            Value="{AppThemeBinding Light={StaticResource Primary},
                                    Dark={StaticResource PrimaryBtnDark}}" />
</DataTrigger>
```

---

## Named Styles Reference

### Typography

`Headline` and `SubHeadline` do **NOT** use `BasedOn="LabelBase"` — they are standalone styles. Only `MutedText`, `SuccessBadge`, `FormLabel`, `CardTitle`, `PickerIndicator`, `PickerSectionHeader`, and `CardsSectionHeader` use `BasedOn="LabelBase"`.

| Style | Size | Weight | BasedOn | Notes |
|---|---|---|---|---|
| `Headline` | 32px (`FontHero`) | — | *(none)* | Centered, SemanticLevel1 |
| `SubHeadline` | 24px (`FontDisplay`) | — | *(none)* | Centered |
| `SectionHeading` | 18px | Bold | *(none)* | SemanticLevel2 |
| `SectionDescription` | 13px | — | *(none)* | Gray secondary |
| `MutedText` | — | — | `LabelBase` | Gray500 / TextMutedDark |
| `FormLabel` | 13px | — | `LabelBase` | Field label |
| `CardTitle` | 17px | Bold | `LabelBase` | Max 3 lines, tail truncation |
| `SuccessBadge` | 10px | — | `LabelBase` | Green success color |
| `PickerIndicator` | 12px | — | `LabelBase` | Decorative ▼ arrow, column 1 |

### Buttons (all `BasedOn="ButtonBase"`)
| Style | Usage | Appearance |
|---|---|---|
| `DangerButton` | Delete/destructive | Red background |
| `GhostButton` | Toolbar actions | Transparent background |
| `CloseButton` | Dismiss/close | Transparent, Gray400/TextMutedDark text, 4px padding |
| `AddPrayerAffordanceButton` | Add-request affordance | Transparent, Primary/PrimaryDark bold 17px text |
| `SmallActionButton` | In-card actions | 13px, compact padding |
| `AnsweredButton` | Mark Answered CTA | `BasedOn="SmallActionButton"`, WarmGold bg |
| `CircleNavButton` | Prayer Time nav | 60×60, 30px radius |

### Layout Styles
| Style | TargetType | Usage |
|---|---|---|
| `PrayerCardBorder` | `Border` | Card wrapper — sets `BackgroundColor` (CardLight/CardDark), 8px corners, no stroke in `Normal`; `Highlighted` / `MultiSelected` visual states add a 2.5px stroke |
| `ActionChip` | `Border` | Rounded action button — 14px corners, 12,8 padding, ChipBg colors |
| `DividerLine` | `BoxView` | 1px rule — `Opacity` via `AppThemeBinding` (Light=0.4, Dark=0.85) |
| `SaveActivityIndicator` | `ActivityIndicator` | Centered; Primary (light) / PrimaryBtnDark (dark) |

### Shell & Navigation (implicit styles)
| TargetType | Key properties |
|---|---|
| `Shell` | TabBarForegroundColor / TabBarTitleColor = WarmGold/WarmGoldDark (selected); TabBarUnselectedColor = Gray900 (light) / TabUnselectedDark (dark); TabBarBackgroundColor = PageLight / TabBarDark |
| `NavigationPage` | BarBackgroundColor = PageLight/PageDark; BarTextColor / IconColor = Tertiary/TextPrimaryDark |
| `Page` (`ApplyToDerivedTypes="True"`) | BackgroundColor = PageLight/PageDark; Padding=0 |

---

## Style Inheritance

```xml
<Style x:Key="ButtonBase" TargetType="Button">
    <!-- Base: mirrors implicit Button style -->
</Style>

<Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light={StaticResource DangerRed}, Dark={StaticResource DangerRed}}" />
</Style>

<Style x:Key="LabelBase" TargetType="Label">
    <!-- Base: mirrors implicit Label style -->
</Style>

<Style x:Key="MutedText" TargetType="Label" BasedOn="{StaticResource LabelBase}">
    <Setter Property="TextColor"
            Value="{AppThemeBinding Light={StaticResource Gray500},
                                    Dark={StaticResource TextMutedDark}}" />
</Style>
```

---

## Value Converters

All seven are registered in `App.xaml` resources:

| Converter class | x:Key | Input → Output |
|---|---|---|
| `BoolToTextDecorationConverter` | `BoolToTextDecoration` | true → Strikethrough, false → None |
| `BoolToMutedColorConverter` | `BoolToMutedColor` | Answered → Gray400/500, Active → OffBlack/White |
| `InverseBoolConverter` | `InverseBool` | true → false, false → true |
| `StringToBoolConverter` | `StringToBool` | non-empty string → true |
| `BoolToChevronConverter` | `BoolToChevron` | true → "▼", false → "›" |
| `BoolToTriangleConverter` | `BoolToTriangle` | true → "▲", false → "▼" |
| `EnumToBoolConverter` | `EnumToBool` | enum value == `ConverterParameter` name → true; drives RadioButton groups |

---

## Font Setup

Registered in `MauiProgram.cs`:

```csharp
fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
```

Default: `OpenSansRegular`. Override with `FontFamily="OpenSansSemibold"` where needed.

---

## Android Platform Colors

`PrayerApp/Platforms/Android/Resources/values/colors.xml`:
- `colorPrimary`: `#6B7D5A` · `colorPrimaryDark`: `#3F4A34` · `colorAccent`: `#C4A052`
- `colorPageLight`: `#FAF8F3` · `colorPageDark`: `#0d0e0c`
- `colorShellAppBar`: `@color/colorPrimary`; `values-night/colors.xml` overrides it to `#212121`

---

## Common Mistakes

| Mistake | Correct approach |
|---|---|
| Assuming `Headline` / `SubHeadline` inherit `LabelBase` | They are standalone styles — `BasedOn` is absent. Only `MutedText`, `SuccessBadge`, `FormLabel`, `CardTitle`, `PickerIndicator`, `PickerSectionHeader`, `CardsSectionHeader` use `LabelBase`. |
| Using static `Opacity="0.4"` on `DividerLine` | `DividerLine` already sets `Opacity` via `AppThemeBinding` (0.4 light, 0.85 dark). Don't override. |
| Assuming `AppThemeBinding` is for colors only | It works on any scalar: `Opacity`, font sizes, strings. See the `DividerLine` style in `Styles.xaml`. |
| `AppThemeBinding` inside a keyed Style inside a `BindableLayout` DataTemplate | Doesn't reliably propagate to all children. Use inline color bindings for `BoxView`/dividers inside `BindableLayout` DataTemplates — the one exception to "no inline colors." |
| Hardcoding hex in XAML or C# | Always use `StaticResource` tokens from Colors.xaml. |
| Referencing a style key before verifying it exists | Always confirm the key is in Styles.xaml or Colors.xaml before using `StaticResource`. |
| Omitting `BackgroundColor` on `PrayerCardBorder` usage | The style already sets `BackgroundColor` (CardLight/CardDark) — do not re-set it inline. |
