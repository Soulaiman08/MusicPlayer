using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MusicPlayer.Views.Controls;

/// <summary>
/// The column layout of one track table: its headings, the live width of every column,
/// which column is selected, and which columns are hidden.
///
/// Every track table in the app shares this shape, so the header control and the row
/// template are written once and bound to whichever <see cref="TrackTableState"/> a page
/// supplies. That keeps resizing, hiding and selection in one code path instead of
/// thirteen near-identical copies, and keeps column indexes logical so an RTL flip does
/// not have to touch any of it.
/// </summary>
public sealed class TrackTableState : INotifyPropertyChanged
{
    private int _active = -1;
    private int _hover = -1;

    public TrackTableState(string key, string[] labels, double[] weights, double[] minimums)
    {
        if (labels.Length != weights.Length || labels.Length != minimums.Length)
            throw new ArgumentException("labels, weights and minimums must line up");

        Key = key;
        Labels = labels;
        Weights = weights;
        Minimums = minimums;

        Widths = new ObservableCollection<GridLength>();
        for (int i = 0; i < labels.Length; i++)
            Widths.Add(new GridLength(weights[i], GridUnitType.Star));
        Widths.Add(new GridLength(40, GridUnitType.Pixel));   // the "..." cell

        Hidden = new ObservableCollection<string>();
    }

    public string Key { get; }
    public string[] Labels { get; }
    public double[] Weights { get; }
    public double[] Minimums { get; }

    /// <summary>Text columns followed by the trailing options cell.</summary>
    public ObservableCollection<GridLength> Widths { get; }

    public ObservableCollection<string> Hidden { get; }

    public int ColumnCount => Labels.Length;

    public double OptionsWidth => Widths[Labels.Length].Value;

    public string ColumnKey(int index) => Key + "Col" + (index + 1);

    public bool IsHidden(int index)
        => index < 0 || index >= Labels.Length || Widths[index].Value <= 0;

    public int Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Raise();
            Raise(nameof(ActiveColumnKey));
        }
    }

    public string ActiveColumnKey => _active >= 0 ? ColumnKey(_active) : string.Empty;

    public int Hover
    {
        get => _hover;
        set
        {
            if (_hover == value) return;
            _hover = value;
            Raise();
        }
    }

    /// <summary>Selects the column, or clears when it is the one already selected.</summary>
    public void ToggleActive(int index)
    {
        if (IsHidden(index)) { Active = -1; return; }
        Active = _active == index ? -1 : index;
    }

    public void ClearSelection() => Active = -1;

    public void SetWidth(int index, GridLength value)
    {
        if (index < 0 || index >= Widths.Count) return;
        double clamped = Math.Max(Minimums[Math.Min(index, Minimums.Length - 1)], value.Value);
        Widths[index] = new GridLength(clamped, value.GridUnitType);
    }

    public void ShowColumn(int index)
    {
        if (index < 0 || index >= ColumnCount) return;
        Widths[index] = new GridLength(Weights[index], GridUnitType.Star);
        Hidden.Remove(ColumnKey(index));
    }

    public void HideColumn(int index)
    {
        if (index < 0 || index >= ColumnCount) return;
        Widths[index] = new GridLength(0, GridUnitType.Pixel);
        if (!Hidden.Contains(ColumnKey(index))) Hidden.Add(ColumnKey(index));
        ClearSelection();
    }

    public void RestoreAll()
    {
        for (int i = 0; i < ColumnCount; i++)
            Widths[i] = new GridLength(Weights[i], GridUnitType.Star);
        Hidden.Clear();
        ClearSelection();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void RaiseWidthsChanged() => Raise(nameof(Widths));
}

/// <summary>
/// Lets a page hand its table state to the shared header and row template. Kept as an
/// attached property so the template can bind to it without knowing which page it is on.
/// </summary>
public static class TrackTable
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(TrackTableState), typeof(TrackTable),
            new PropertyMetadata(null));

    public static TrackTableState? GetState(DependencyObject element)
        => (TrackTableState?)element.GetValue(StateProperty);

    public static void SetState(DependencyObject element, TrackTableState? value)
        => element.SetValue(StateProperty, value);
}