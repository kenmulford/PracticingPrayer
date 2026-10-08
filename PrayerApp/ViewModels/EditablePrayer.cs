using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PrayerApp.ViewModels;

public class EditablePrayer : ObservableObject
{
    public EditablePrayer()
    {
        ExpandDetailsCommand = new RelayCommand(() => IsDetailsExpanded = true);
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(AddAccessibleDescription));
                OnPropertyChanged(nameof(RemoveAccessibleDescription));
            }
        }
    }

    // 0-based position in the import payload; -1 for rows the user typed. Lets
    // a re-match order skipped rows the way the share listed them (#313).
    public int PayloadIndex { get; init; } = -1;

    // True while the row sits in To import after "+ Add" moved it from
    // Already on this card; ✕ on such a row returns it instead of deleting it.
    private bool _isAddedDuplicate;
    public bool IsAddedDuplicate
    {
        get => _isAddedDuplicate;
        set
        {
            if (SetProperty(ref _isAddedDuplicate, value))
                OnPropertyChanged(nameof(RemoveAccessibleDescription));
        }
    }

    public string AddAccessibleDescription => $"Add {Title} to import";

    private string? _details;
    public string? Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }

    // Per-row Details disclosure (#17 / UX-38). Default false (collapsed); the
    // import construction site seeds it true when the parser prefilled Details.
    // Deliberately a PLAIN settable bool — independent of the Details text — so
    // editing Details never auto-collapses/expands the row mid-edit. The "+ details"
    // affordance (collapsed) and the Details Editor (expanded) bind to this.
    private bool _isDetailsExpanded;
    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set => SetProperty(ref _isDetailsExpanded, value);
    }

    public RelayCommand ExpandDetailsCommand { get; }

    // Position (1-based) of this row within the Prayers collection and the
    // current total row count, stamped by ConfirmImportViewModel whenever the
    // collection changes. They fold into the accessible descriptions below so a
    // screen-reader user hears a positional cue ("item 2 of 3") instead of the
    // same label on every row (#15 / A11Y-5).
    private int _position;
    public int Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
                RaiseAccessibleDescriptionsChanged();
        }
    }

    private int _total;
    public int Total
    {
        get => _total;
        set
        {
            if (SetProperty(ref _total, value))
                RaiseAccessibleDescriptionsChanged();
        }
    }

    // Computed accessible labels — the visible field has no text label, so
    // SemanticProperties.Description is the correct accessible name. The
    // "{n} of {count}" phrasing mirrors PrayerTimeViewModel.ProgressDisplay.
    public string TitleAccessibleDescription => $"Prayer title, item {Position} of {Total}";
    public string DetailsAccessibleDescription => $"Prayer details, item {Position} of {Total}";
    // ✕ on an added duplicate returns it to Already on this card instead of deleting it.
    public string RemoveAccessibleDescription => IsAddedDuplicate
        ? $"Return {Title} to Already on this card, item {Position} of {Total}"
        : $"Remove prayer, item {Position} of {Total}";

    private void RaiseAccessibleDescriptionsChanged()
    {
        OnPropertyChanged(nameof(TitleAccessibleDescription));
        OnPropertyChanged(nameof(DetailsAccessibleDescription));
        OnPropertyChanged(nameof(RemoveAccessibleDescription));
    }
}
