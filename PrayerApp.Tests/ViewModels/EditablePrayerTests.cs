using System.ComponentModel;
using PrayerApp.ViewModels;

namespace PrayerApp.Tests.ViewModels;

public class EditablePrayerTests
{
    // ── Accessible descriptions (#15) ──────────────────────────

    [Fact]
    public void TitleAccessibleDescription_FoldsPositionAndTotal()
    {
        var row = new EditablePrayer { Position = 2, Total = 3 };

        Assert.Equal("Prayer title, item 2 of 3", row.TitleAccessibleDescription);
    }

    [Fact]
    public void DetailsAccessibleDescription_FoldsPositionAndTotal()
    {
        var row = new EditablePrayer { Position = 2, Total = 3 };

        Assert.Equal("Prayer details, item 2 of 3", row.DetailsAccessibleDescription);
    }

    [Fact]
    public void RemoveAccessibleDescription_FoldsPositionAndTotal()
    {
        var row = new EditablePrayer { Position = 2, Total = 3 };

        Assert.Equal("Remove prayer, item 2 of 3", row.RemoveAccessibleDescription);
    }

    [Fact]
    public void RemoveAccessibleDescription_AddedDuplicate_ReadsReturnToAlreadyOnCard()
    {
        var row = new EditablePrayer { Title = "Healing", Position = 2, Total = 3 };
        Assert.Equal("Remove prayer, item 2 of 3", row.RemoveAccessibleDescription);

        row.IsAddedDuplicate = true;

        Assert.Equal("Return Healing to Already on this card, item 2 of 3", row.RemoveAccessibleDescription);
    }

    [Fact]
    public void RemoveAccessibleDescription_RaisedWhenIsAddedDuplicateOrTitleChanges()
    {
        var row = new EditablePrayer { Position = 1, Total = 1 };
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.IsAddedDuplicate = true;
        Assert.Contains(nameof(EditablePrayer.RemoveAccessibleDescription), raised);

        raised.Clear();
        row.Title = "Healing";
        Assert.Contains(nameof(EditablePrayer.RemoveAccessibleDescription), raised);
    }

    [Fact]
    public void SettingPosition_RaisesPropertyChangedForAllThreeDescriptions()
    {
        var row = new EditablePrayer { Total = 3 };
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Position = 2;

        Assert.Contains(nameof(EditablePrayer.TitleAccessibleDescription), raised);
        Assert.Contains(nameof(EditablePrayer.DetailsAccessibleDescription), raised);
        Assert.Contains(nameof(EditablePrayer.RemoveAccessibleDescription), raised);
    }

    [Fact]
    public void SettingTotal_RaisesPropertyChangedForAllThreeDescriptions()
    {
        var row = new EditablePrayer { Position = 1 };
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Total = 4;

        Assert.Contains(nameof(EditablePrayer.TitleAccessibleDescription), raised);
        Assert.Contains(nameof(EditablePrayer.DetailsAccessibleDescription), raised);
        Assert.Contains(nameof(EditablePrayer.RemoveAccessibleDescription), raised);
    }

    // ── IsDetailsExpanded / ExpandDetailsCommand (#17 / UX-38) ──────────────

    [Fact]
    public void IsDetailsExpanded_DefaultsToFalse()
    {
        var row = new EditablePrayer();

        Assert.False(row.IsDetailsExpanded);
    }

    [Fact]
    public void ExpandDetailsCommand_SetsIsDetailsExpandedTrue()
    {
        var row = new EditablePrayer();

        row.ExpandDetailsCommand.Execute(null);

        Assert.True(row.IsDetailsExpanded);
    }

    [Fact]
    public void ExpandDetailsCommand_RaisesPropertyChangedForIsDetailsExpanded()
    {
        var row = new EditablePrayer();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.ExpandDetailsCommand.Execute(null);

        Assert.Contains(nameof(EditablePrayer.IsDetailsExpanded), raised);
    }

    [Fact]
    public void IsDetailsExpanded_IsSettableAndStableAcrossDetailsEdits()
    {
        // Editing Details must NOT auto-collapse/expand the row — the flag is a
        // plain settable bool, independent of the Details text (stability while
        // the user types). Once expanded, typing leaves it expanded.
        var row = new EditablePrayer { IsDetailsExpanded = true };

        row.Details = "now has text";
        Assert.True(row.IsDetailsExpanded);

        row.Details = string.Empty;
        Assert.True(row.IsDetailsExpanded);
    }
}
