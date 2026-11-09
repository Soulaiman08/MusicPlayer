﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MusicPlayer.Library;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using MusicPlayer.Views.Controls;

namespace MusicPlayer.Views
{
    public partial class MainWindow
    {
    private void HideTableColumn_Click(object sender, RoutedEventArgs e)    {
        if (sender is not FrameworkElement element || element.Tag is not string key) return;
        var dp = ColumnPropertyFor(key);
        if (dp == null) return;
        bool isSongs = key.StartsWith("SongsCol", StringComparison.Ordinal);
        if (!_savedColumnWidths.ContainsKey(key)) _savedColumnWidths[key] = (GridLength)GetValue(dp);
        SetValue(dp, new GridLength(0));
        var list = isSongs ? SongsHiddenColumns : AlbumHiddenColumns;
        if (!list.Contains(key)) list.Add(key);

        // The selected column has just collapsed, so its two arrows would keep
        // floating over the neighbour's boundary. Hiding behaves like a plain
        // deselect - it must not pick some other column at random.
        SetValue(isSongs ? SongsActiveColumnProperty : AlbumActiveColumnProperty, -1);

        RaiseHiddenColumnsChanged();
        e.Handled = true;
    }
    // until the user drags a handle, which pins that column to a pixel width.
    // Header and row grids bind to the same properties, so a drag on the header
    // handle re-flows every row at once.
    private static DependencyProperty ColWidthProp(string name, double value, GridUnitType unit = GridUnitType.Star)
        => DependencyProperty.Register(name, typeof(GridLength), typeof(MainWindow),
               new PropertyMetadata(new GridLength(value, unit)));

    public static readonly DependencyProperty SongsCol1WidthProperty = ColWidthProp("SongsCol1Width", 2.8);
    public static readonly DependencyProperty SongsCol2WidthProperty = ColWidthProp("SongsCol2Width", 2.6);
    public static readonly DependencyProperty SongsCol3WidthProperty = ColWidthProp("SongsCol3Width", 2.0);
    public static readonly DependencyProperty SongsCol4WidthProperty = ColWidthProp("SongsCol4Width", 1.0);
    public static readonly DependencyProperty SongsCol5WidthProperty = ColWidthProp("SongsCol5Width", 0.9);

    public static readonly DependencyProperty AlbumCol1WidthProperty = ColWidthProp("AlbumCol1Width", 2.6);
    public static readonly DependencyProperty AlbumCol2WidthProperty = ColWidthProp("AlbumCol2Width", 2.4);
    public static readonly DependencyProperty AlbumCol3WidthProperty = ColWidthProp("AlbumCol3Width", 1.6);
    public static readonly DependencyProperty AlbumCol4WidthProperty = ColWidthProp("AlbumCol4Width", 0.9);

    public GridLength SongsCol1Width { get => (GridLength)GetValue(SongsCol1WidthProperty); set => SetValue(SongsCol1WidthProperty, value); }
    public GridLength SongsCol2Width { get => (GridLength)GetValue(SongsCol2WidthProperty); set => SetValue(SongsCol2WidthProperty, value); }
    public GridLength SongsCol3Width { get => (GridLength)GetValue(SongsCol3WidthProperty); set => SetValue(SongsCol3WidthProperty, value); }
    public GridLength SongsCol4Width { get => (GridLength)GetValue(SongsCol4WidthProperty); set => SetValue(SongsCol4WidthProperty, value); }
    public GridLength SongsCol5Width { get => (GridLength)GetValue(SongsCol5WidthProperty); set => SetValue(SongsCol5WidthProperty, value); }
    public GridLength AlbumCol1Width { get => (GridLength)GetValue(AlbumCol1WidthProperty); set => SetValue(AlbumCol1WidthProperty, value); }
    public GridLength AlbumCol2Width { get => (GridLength)GetValue(AlbumCol2WidthProperty); set => SetValue(AlbumCol2WidthProperty, value); }
    public GridLength AlbumCol3Width { get => (GridLength)GetValue(AlbumCol3WidthProperty); set => SetValue(AlbumCol3WidthProperty, value); }
    public GridLength AlbumCol4Width { get => (GridLength)GetValue(AlbumCol4WidthProperty); set => SetValue(AlbumCol4WidthProperty, value); }

    // The trailing "options" cell takes part in resizing too, so the arrow on the
    // last column's right edge has something to trade with.
    public static readonly DependencyProperty SongsOptionsWidthProperty = ColWidthProp("SongsOptionsWidth", 40, GridUnitType.Pixel);
    public static readonly DependencyProperty AlbumOptionsWidthProperty = ColWidthProp("AlbumOptionsWidth", 40, GridUnitType.Pixel);

    /// <summary>Index of the column the pointer is over, -1 when none.</summary>
    public static readonly DependencyProperty SongsHoverColumnProperty = HoverColumnProp("SongsHoverColumn");
    public static readonly DependencyProperty AlbumHoverColumnProperty = HoverColumnProp("AlbumHoverColumn");

    private static DependencyProperty HoverColumnProp(string name)
        => DependencyProperty.Register(name, typeof(int), typeof(MainWindow), new PropertyMetadata(-1));

    public int SongsHoverColumn
    {
        get => (int)GetValue(SongsHoverColumnProperty);
        set => SetValue(SongsHoverColumnProperty, value);
    }

    public int AlbumHoverColumn
    {
        get => (int)GetValue(AlbumHoverColumnProperty);
        set => SetValue(AlbumHoverColumnProperty, value);
    }

    public GridLength SongsOptionsWidth { get => (GridLength)GetValue(SongsOptionsWidthProperty); set => SetValue(SongsOptionsWidthProperty, value); }
    public GridLength AlbumOptionsWidth { get => (GridLength)GetValue(AlbumOptionsWidthProperty); set => SetValue(AlbumOptionsWidthProperty, value); }

    // ---- the column the user selected in each table (-1 = none) ----------------
    private static DependencyProperty ActiveColumnProp(string name)
        => DependencyProperty.Register(name, typeof(int), typeof(MainWindow), new PropertyMetadata(-1));

    public static readonly DependencyProperty SongsActiveColumnProperty = ActiveColumnProp("SongsActiveColumn");
    public static readonly DependencyProperty AlbumActiveColumnProperty = ActiveColumnProp("AlbumActiveColumn");

    /// <summary>
    /// Index of the column whose resize arrows are showing, or -1. Clicking a
    /// header picks that column; clicking it again clears the selection - the
    /// same one-column-at-a-time behaviour Harmonoid uses.
    /// </summary>
    public int SongsActiveColumn { get => (int)GetValue(SongsActiveColumnProperty); set => SetValue(SongsActiveColumnProperty, value); }

    /// <summary>Same selection state for the album-detail table.</summary>
    public int AlbumActiveColumn { get => (int)GetValue(AlbumActiveColumnProperty); set => SetValue(AlbumActiveColumnProperty, value); }

    // ---- per-table hidden columns (session only) ------------------------------
    public event PropertyChangedEventHandler? PropertyChanged;

    private ObservableCollection<string> _songsHiddenColumns = new();
    private ObservableCollection<string> _albumHiddenColumns = new();
    private readonly Dictionary<string, GridLength> _savedColumnWidths = new();

    public ObservableCollection<string> SongsHiddenColumns
    {
        get => _songsHiddenColumns;
        set { _songsHiddenColumns = value; RaiseHiddenColumnsChanged(); }
    }

    public ObservableCollection<string> AlbumHiddenColumns
    {
        get => _albumHiddenColumns;
        set { _albumHiddenColumns = value; RaiseHiddenColumnsChanged(); }
    }

    private static readonly string[] SongsColumnKeys =
        { "SongsCol1Width", "SongsCol2Width", "SongsCol3Width", "SongsCol4Width", "SongsCol5Width" };

    private static readonly string[] AlbumColumnKeys =
        { "AlbumCol1Width", "AlbumCol2Width", "AlbumCol3Width", "AlbumCol4Width" };

    public static readonly DependencyProperty SongsHiddenColumnCountProperty = HiddenCountProp("SongsHiddenColumnCount");
    public static readonly DependencyProperty AlbumHiddenColumnCountProperty = HiddenCountProp("AlbumHiddenColumnCount");

    public int SongsHiddenColumnCount { get => (int)GetValue(SongsHiddenColumnCountProperty); private set => SetValue(SongsHiddenColumnCountProperty, value); }
    public int AlbumHiddenColumnCount { get => (int)GetValue(AlbumHiddenColumnCountProperty); private set => SetValue(AlbumHiddenColumnCountProperty, value); }


    /// <summary>Text-column index of a width property (options excluded), or -1.</summary>
    private static int IndexOfColumnProperty(DependencyProperty dp, DependencyProperty[] props)
    {
        for (int i = 0; i < props.Length - 1; i++) if (ReferenceEquals(props[i], dp)) return i;
        return -1;
    }

    private void SongsColumns_Restore_Click(object sender, RoutedEventArgs e) => RestoreColumns(SongsColumnKeys, SongsHiddenColumns);

    private void SongsColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        SongsColumnsMenu.IsOpen = false;
        SongsColumnsMenu.IsOpen = true;
        e.Handled = true;
    }

    private void AlbumColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        AlbumColumnsMenu.IsOpen = false;
        AlbumColumnsMenu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Ticks every box to match the columns that are actually shown.</summary>
    private void SongsColumnsMenu_Opened(object sender, EventArgs e)
    {
        SongsChk1.IsChecked = IsColumnVisible(SongsCol1WidthProperty);
        SongsChk2.IsChecked = IsColumnVisible(SongsCol2WidthProperty);
        SongsChk3.IsChecked = IsColumnVisible(SongsCol3WidthProperty);
        SongsChk4.IsChecked = IsColumnVisible(SongsCol4WidthProperty);
        SongsChk5.IsChecked = IsColumnVisible(SongsCol5WidthProperty);
    }

    private void AlbumColumnsMenu_Opened(object sender, EventArgs e)
    {
        AlbumChk1.IsChecked = IsColumnVisible(AlbumCol1WidthProperty);
        AlbumChk2.IsChecked = IsColumnVisible(AlbumCol2WidthProperty);
        AlbumChk3.IsChecked = IsColumnVisible(AlbumCol3WidthProperty);
        AlbumChk4.IsChecked = IsColumnVisible(AlbumCol4WidthProperty);
    }

    private bool IsColumnVisible(DependencyProperty dp)
        => ((GridLength)GetValue(dp)).Value > 0;

    /// <summary>Ticking a box shows that column, unticking hides it - same as the X.</summary>
    private void ColumnOption_Checked(object sender, RoutedEventArgs e) => SetColumnFromPicker(sender, show: true);
    private void ColumnOption_Unchecked(object sender, RoutedEventArgs e) => SetColumnFromPicker(sender, show: false);

    private void SetColumnFromPicker(object sender, bool show)
    {
        if (sender is not FrameworkElement element || element.Tag is not string key) return;
        if (ColumnPropertyFor(key) is not { } dp) return;
        bool isSongs = key.StartsWith("SongsCol", StringComparison.Ordinal);
        bool visible = ((GridLength)GetValue(dp)).Value > 0;

        if (show && !visible)
        {
            if (!_savedColumnWidths.TryGetValue(key, out var saved) || saved.Value <= 0)
                saved = new GridLength(DefaultColumnWidth(isSongs, key));
            SetValue(dp, saved);
            _savedColumnWidths.Remove(key);
            RemoveHidden(isSongs, key);
        }
        else if (!show && visible)
        {
            if (!_savedColumnWidths.ContainsKey(key)) _savedColumnWidths[key] = (GridLength)GetValue(dp);
            SetValue(dp, new GridLength(0));
            var list = isSongs ? SongsHiddenColumns : AlbumHiddenColumns;
            if (!list.Contains(key)) list.Add(key);
        }
        else return;

        RaiseHiddenColumnsChanged();
    }

    private void RemoveHidden(bool isSongs, string key)
    {
        var list = isSongs ? SongsHiddenColumns : AlbumHiddenColumns;
        if (list.Remove(key)) { /* count refreshed by RaiseHiddenColumnsChanged */ }
    }

    private double DefaultColumnWidth(bool isSongs, string key)
    {
        var props = isSongs ? SongsColProps : AlbumColProps;
        if (ColumnPropertyFor(key) is not { } dp) return 120;
        for (int i = 0; i < props.Length - 1; i++)
            if (ReferenceEquals(props[i], dp)) return DefaultColumnWeights(isSongs, i);
        return 120;
    }

    private static readonly double[] SongsColumnWeights = { 2.8, 2.6, 2.0, 1.0, 0.9 };
    private static readonly double[] AlbumColumnWeights = { 2.6, 2.4, 1.6, 0.9 };

    private static double DefaultColumnWeights(bool isSongs, int index)
    {
        var w = isSongs ? SongsColumnWeights : AlbumColumnWeights;
        return index >= 0 && index < w.Length ? w[index] * 110.0 : 120.0;
    }

    private void AlbumColumns_Restore_Click(object sender, RoutedEventArgs e) => RestoreColumns(AlbumColumnKeys, AlbumHiddenColumns);

    private void RestoreColumns(string[] keys, ObservableCollection<string> list)
    {
        foreach (var key in keys)
        {
            if (_savedColumnWidths.TryGetValue(key, out var width) && ColumnPropertyFor(key) is { } dp)
            {
                SetValue(dp, width);
            }
        }
        foreach (var key in keys) _savedColumnWidths.Remove(key);
        list.Clear();
        RaiseHiddenColumnsChanged();
    }

    private static DependencyProperty? ColumnPropertyFor(string key)
    {
        foreach (var dp in SongsColProps.Concat(AlbumColProps))
        {
            if (dp.Name == key) return dp;
        }
        return null;
    }

    private void RaiseHiddenColumnsChanged()
    {
        SongsHiddenColumnCount = SongsHiddenColumns.Count;
        AlbumHiddenColumnCount = AlbumHiddenColumns.Count;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SongsHiddenColumns)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlbumHiddenColumns)));

        // Collapsing a column frees up width. If the columns had already been frozen
        // to pixels by a drag, that width would just sit empty and push the fixed
        // options ("...") column away from the edge - so let the remaining columns
        // take it back up once the layout has applied the new widths.
        _songsSync.NeedsRefit = true;
        _albumSync.NeedsRefit = true;
        foreach (var st in _sharedSyncs) st.NeedsRefit = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateTracksScrollbarInset));
    }

    private static DependencyProperty HiddenCountProp(string name)
        => DependencyProperty.Register(name, typeof(int), typeof(MainWindow), new PropertyMetadata(0));

    private static Grid? FindAncestorGrid(object element)
    {
        DependencyObject? current = element as DependencyObject;
        while (current is not null)
        {
            if (current is Grid g && g.Name.EndsWith("HeaderGrid", StringComparison.Ordinal)) return g;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public static readonly DependencyProperty IsSongsGridViewProperty =
        DependencyProperty.Register("IsSongsGridView", typeof(bool), typeof(MainWindow),
            new PropertyMetadata(false));

    /// <summary>
    /// Width of the tracks list's vertical scrollbar. The table header lives
    /// outside the list, so without this nudge its separators drift a few pixels
    /// away from the ones drawn between the rows.
    /// </summary>
    public static readonly DependencyProperty TracksScrollbarWidthProperty =
        DependencyProperty.Register("TracksScrollbarWidth", typeof(double), typeof(MainWindow),
            new PropertyMetadata(0.0));

    public double TracksScrollbarWidth
    {
        get => (double)GetValue(TracksScrollbarWidthProperty);
        private set => SetValue(TracksScrollbarWidthProperty, value);
    }

private void SongsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
    UpdateTracksScrollbarInset();
    // Scrolling carries the user away from the header they were editing.
    ClearColumnSelection();
    }

    /// <summary>
    /// Width of the tracks list's viewport, kept in sync with the header grid.
    /// </summary>
    public static readonly DependencyProperty SongsViewportWidthProperty =
        DependencyProperty.Register("SongsViewportWidth", typeof(double), typeof(MainWindow),
            // NaN keeps the header at its natural (stretch) width until the first
            // measurement arrives, instead of collapsing to zero.
            new PropertyMetadata(double.NaN));

    public double SongsViewportWidth
    {
        get => (double)GetValue(SongsViewportWidthProperty);
        private set => SetValue(SongsViewportWidthProperty, value);
    }

    private void UpdateTracksScrollbarInset()
    {
        SyncHeaderToViewport(_songsSync);
        SyncHeaderToViewport(_albumSync);
        foreach (var st in _sharedSyncs) SyncHeaderToViewport(st);
    }

    /// <summary>
    /// Puts a shared table's header under the same sync as the two hand-written ones:
    /// the header adopts the exact rectangle the rows are laid out in, so the column
    /// separators line up instead of drifting by the scrollbar's width.
    /// </summary>
    private void RegisterSharedSync(TrackTableHeader? header, FrameworkElement? list, TrackTableState state)
    {
        if (header is null || list is null) return;

        var st = new HeaderSync
        {
            List = list,
            Header = header,
            Access = new ColumnAccess { State = state, Owner = this },
        };
        _sharedSyncs.Add(st);

        list.SizeChanged += (_, _) => SyncHeaderToViewport(st);
        list.Loaded += (_, _) => SyncHeaderToViewport(st);
        list.IsVisibleChanged += (_, _) => SyncHeaderToViewport(st);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => SyncHeaderToViewport(st)));
    }

    /// <summary>Per-table state for <see cref="SyncHeaderToViewport"/>.</summary>
    private sealed class HeaderSync
    {
        public FrameworkElement? List;      // the row container (ListBox / ItemsControl)
        public FrameworkElement? Header;    // the header (Grid or TrackTableHeader)
        public ScrollViewer? Scroller;
        public bool Hooked, Pending;
        public int Retries;
        public ColumnAccess? Access;
        public double LastViewport = double.NaN;
        public bool NeedsRefit;
    }

    private readonly HeaderSync _songsSync = new();
    private readonly HeaderSync _albumSync = new();
    private readonly List<HeaderSync> _sharedSyncs = new();

    /// <summary>
    /// A table's header sits outside its list, so it has to adopt the exact
    /// rectangle the rows are laid out in - the same width the list gives up to its
    /// vertical scrollbar, starting at the same x (the ScrollViewer can sit a pixel
    /// inside the ListBox). Otherwise every column separator is a pixel or two off
    /// and the line visibly jogs where the header meets the first row.
    /// </summary>
    private void SyncHeaderToViewport(HeaderSync st)
    {
        try { SyncHeaderToViewportCore(st); }
        catch (Exception ex) { LogHelper.LogError("Header sync failed", ex); }
    }

    private void SyncHeaderToViewportCore(HeaderSync st)
    {
        if (st.List is null || st.Header is null) return;
        st.Scroller ??= FindVisualChild<ScrollViewer>(st.List);
        if (st.Scroller is null)
        {
            // The list is only realised once its page is opened.
            ScheduleHeaderRetry(st);
            return;
        }
        if (!st.Hooked)
        {
            st.Hooked = true;
            var scroller = st.Scroller;
            // The gutter appears and disappears without resizing the ScrollViewer,
            // so these are only triggers - the values are re-read below.
            scroller.SizeChanged += (_, _) => SyncHeaderToViewport(st);
            // Creating the scrollbar is reported as a viewport change, which is the
            // only signal that ViewportWidth just got smaller.
            scroller.ScrollChanged += (_, _) => SyncHeaderToViewport(st);
            scroller.ScrollChanged += (_, _) => ClearColumnSelection();
        }
        double viewport = st.Scroller.ViewportWidth;
        if (viewport <= 0)
        {
            // ViewportWidth is only valid once the ScrollViewer has been measured;
            // SizeChanged fires before that, so retry after the layout pass.
            ScheduleHeaderRetry(st);
            return;
        }
        double left = st.Scroller.TranslatePoint(new Point(0, 0), st.List).X;
        if (st.NeedsRefit
            || (!double.IsNaN(st.LastViewport) && Math.Abs(st.LastViewport - viewport) > 0.5))
            RefitFrozenColumns(st, viewport);
        st.NeedsRefit = false;
        st.LastViewport = viewport;
        if (Math.Abs(st.Header.Width - viewport) < 0.01
            && Math.Abs(st.Header.Margin.Left - left) < 0.01)
            return; // converged

        st.Header.Margin = new Thickness(left, 0, 0, 0);
        st.Header.HorizontalAlignment = HorizontalAlignment.Left;
        st.Header.Width = viewport;
        if (st.List == AllSongsListBox)
        {
            SongsViewportWidth = viewport;
            TracksScrollbarWidth = Math.Max(0, AllSongsListBox.ActualWidth - viewport);
        }
        st.Retries = 0;
        // The scrollbar may only appear once the items are realised, and that does
        // not resize anything - re-check until the value stops changing.
        ScheduleHeaderRetry(st);
    }

    /// <summary>
    /// Once a column has been dragged its width is frozen to pixels, so the row of
    /// columns stops following the window and the fixed art / options columns drift
    /// away from their edges. Rescale the dragged columns proportionally to the new
    /// viewport whenever the window changes size: the art cell and the options cell
    /// keep their fixed widths (so the "..." column stays pinned), and the text
    /// columns share whatever is left. Star columns already do this by themselves.
    /// </summary>
    private void RefitFrozenColumns(HeaderSync st, double viewport)
    {
        var access = st.Access;
        if (access is null || st.Header is null) return;
        int text = access.TextCount;                // the options cell is not a text column

        // Only act once the widths really are pixels; star widths self-refit.
        double sum = 0;
        for (int i = 0; i < text; i++)
        {
            if (!access.Get(i).IsAbsolute) return;
            double v = access.Get(i).Value;
            if (v <= 0) continue;                 // hidden column: keep it collapsed
            sum += v;
        }
        if (sum <= 0) return;

        double fixedCells = ArtColumnWidth(st.Header);
        // The options cell is excluded from `sum`, so its width has to come off the
        // available space as well - otherwise the text columns creep ~3% wider on
        // every window resize.
        var options = access.Get(text);
        if (options.IsAbsolute) fixedCells += options.Value;
        double avail = viewport - fixedCells;
        if (avail <= 0) return;

        double k = avail / sum;
        for (int i = 0; i < text; i++)
        {
            var current = access.Get(i);
            if (current.Value <= 0) continue;      // hidden column stays at zero
            double w = current.Value * k;
            access.Set(i, new GridLength(Math.Max(access.State.Minimums[i], w)));
        }
    }

    /// <summary>
    /// Width of the fixed art cell. The legacy headers keep it in their first grid
    /// column; the shared header always uses the same 48px cell.
    /// </summary>
    private static double ArtColumnWidth(FrameworkElement? header)
        => header is Grid g && g.ColumnDefinitions.Count > 0
            ? ((GridLength)g.ColumnDefinitions[0].Width).Value
            : 48.0;

    /// <summary>
    /// Drops the column selection on both tables. The arrows and the hide "X" are a
    /// live editing affordance, so anything that takes the user out of the header -
    /// navigating, switching tab, scrolling, Esc, clicking a row - ends it.
    /// </summary>
    internal void ClearColumnSelection()
    {
        // A live resize owns the selection: dropping it now would collapse the
        // handle the user is holding and abort the gesture.
        if (_isResizing) return;
        if (SongsActiveColumn >= 0) SongsActiveColumn = -1;
        if (AlbumActiveColumn >= 0) AlbumActiveColumn = -1;
        foreach (var table in TrackTables) table.ClearSelection();
    }

    /// <summary>True when the press landed on a header cell, a resize handle or the hide button.</summary>
    private static bool IsInsideColumnHeader(object? source)
    {
        for (DependencyObject? node = source as DependencyObject; node is not null;)
        {
            if (node is Thumb) return true;
            if (node is TrackTableHeader) return true;
            if (node is FrameworkElement { Name: "SongsHeaderGrid" or "AlbumHeaderGrid" }) return true;
            if (node is FrameworkElement fe && fe.Tag is string t
                && (t.StartsWith("SongsHeaderGrid", StringComparison.Ordinal)
                 || t.StartsWith("AlbumHeaderGrid", StringComparison.Ordinal))) return true;
            node = VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent;
        }
        return false;
    }

    /// <summary>Clicking anywhere outside the header row ends the column selection.</summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsInsideColumnHeader(e.OriginalSource)) ClearColumnSelection();
    }

    private void SongsHeaderCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag
            && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            SongsHoverColumn = index;
    }

    private void AlbumHeaderCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag
            && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            AlbumHoverColumn = index;
    }

    private void HeaderCell_MouseLeave(object sender, MouseEventArgs e)
    {
        SongsHoverColumn = -1;
        AlbumHoverColumn = -1;
    }

    private void ScheduleHeaderRetry(HeaderSync st)
    {
        if (st.Pending || st.Retries >= 12) return;
        st.Pending = true;
        st.Retries++;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            st.Pending = false;
            SyncHeaderToViewport(st);
        }));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(parent, i) is T typed) return typed;
            if (FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i)) is T nested) return nested;
        }
        return null;
    }

    /// <summary>False shows the tracks table, true shows the album-art card grid.</summary>
    public bool IsSongsGridView
    {
        get => (bool)GetValue(IsSongsGridViewProperty);
        set => SetValue(IsSongsGridViewProperty, value);
    }

    /// <summary>Switches the tracks view between the column table and the card grid.</summary>
    private void ToggleSongsLayout_Click(object sender, RoutedEventArgs e)
    {
        bool showGrid = !IsSongsGridView;
        string mode = showGrid ? "grid" : "list";
        AlbumArtUiProbe? imageProbe = showGrid && ViewModel?.CurrentView == "songs"
            ? BeginTracksImageProbe("mode-toggle", mode)
            : null;

        IsSongsGridView = showGrid;
        SongsActiveColumn = -1;
        if (ViewModel?.CurrentView == "songs")
            BeginTracksSurfaceProbe("mode-toggle", mode, imageProbe);
    }

    private static readonly DependencyProperty[] SongsColProps =
    {
        SongsCol1WidthProperty, SongsCol2WidthProperty, SongsCol3WidthProperty,
        SongsCol4WidthProperty, SongsCol5WidthProperty, SongsOptionsWidthProperty
    };

    private static readonly DependencyProperty[] AlbumColProps =
    {
        AlbumCol1WidthProperty, AlbumCol2WidthProperty, AlbumCol3WidthProperty,
        AlbumCol4WidthProperty, AlbumOptionsWidthProperty
    };

    /// <summary>
    /// Picks the clicked column (its two arrows appear) or clears the selection
    /// when the same header is clicked again.
    /// </summary>
    private void ColumnHeader_ToggleEditMode(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag) return;
        if (!int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int columnIndex)) return;

bool isSongs = FindAncestorGrid(sender)?.Name == "SongsHeaderGrid";
    var property = isSongs ? SongsActiveColumnProperty : AlbumActiveColumnProperty;
    int current = (int)GetValue(property);

    // Guard: a hidden column is 0px wide, so it has no cell to click and no edge to
    // anchor its handles to - selecting one is what produced the half-clipped arrow.
    // Checked here, inside the setter, so every path that can set it is covered.
    if (IsColumnHidden(isSongs, columnIndex))
    {
        SetValue(property, -1);
        e.Handled = true;
        return;
    }

    SetValue(property, current == columnIndex ? -1 : columnIndex);
    e.Handled = true;
    }

    private bool IsColumnHidden(bool isSongs, int index)
    {
        var props = isSongs ? SongsColProps : AlbumColProps;
        if (index < 0 || index >= props.Length - 1) return true;   // options cell never selects
        return ((GridLength)GetValue(props[index])).Value <= 0;
    }


    private void SongsRow_Hide_Click(object sender, RoutedEventArgs e) => HideTableColumn_Click(sender, e);

    private void AlbumRow_Hide_Click(object sender, RoutedEventArgs e) => HideTableColumn_Click(sender, e);

    private void SongsRows_Restore_Click(object sender, RoutedEventArgs e) => SongsColumns_Restore_Click(sender, e);
    private void AlbumRows_Restore_Click(object sender, RoutedEventArgs e) => AlbumColumns_Restore_Click(sender, e);

    private static readonly double[] SongsColumnMinimums = { 130, 110, 110, 80, 76, 24 };
    private static readonly double[] AlbumColumnMinimums = { 130, 110, 90, 76, 24 };

    /// <summary>
    /// Every track table shares one column layout description, so the shared header
    /// control is told what to draw and the shared row template reads the same widths
    /// back. Keys are stable identifiers used for saved column keys, nothing more.
    /// </summary>
    private static readonly string[] StandardColumns = { "Title", "Artists", "Album", "Genres" };
    private static readonly double[] StandardWeights = { 2.8, 2.6, 2.0, 1.0 };
    private static readonly double[] StandardMinimums = { 130, 110, 110, 80 };

    /// <summary>
    /// The legacy minimum arrays carry a trailing value for the fixed "..." cell; a table
    /// state only tracks text columns, so the extra entry is dropped here.
    /// </summary>
    private static double[] TextMinimums(double[] withOptions, int columns)
        => withOptions.Take(columns).ToArray();

    public TrackTableState SongsTable { get; } =
        new("Songs", new[] { "Title", "Artists", "Album", "Genres", "Year" },
            new[] { 2.8, 2.6, 2.0, 1.0, 0.9 }, TextMinimums(SongsColumnMinimums, 5));

    public TrackTableState AlbumTable { get; } =
        new("Album", new[] { "Title", "Artists", "Genres", "Year" },
            new[] { 2.6, 2.4, 1.6, 0.9 }, TextMinimums(AlbumColumnMinimums, 4));

    public TrackTableState RecentPlayTable { get; } =
        new("RecentPlay", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState RecentAddTable { get; } =
        new("RecentAdd", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState MostPlayedTable { get; } =
        new("MostPlayed", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState FavoritesTable { get; } =
        new("Favorites", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState ArtistDetailTable { get; } =
        new("ArtistDetail", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState FolderDetailTable { get; } =
        new("FolderDetail", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState AlbumMusicTable { get; } =
        new("AlbumMusic", StandardColumns, StandardWeights, StandardMinimums);
    public TrackTableState PlaylistDetailTable { get; } =
        new("PlaylistDetail", new[] { "Title", "Artists", "Album", "Genres", "Time" },
            new[] { 2.6, 2.4, 1.8, 1.1, 0.9 }, new double[] { 130, 110, 110, 90, 84 });
    public TrackTableState GenreDetailTable { get; } =
        new("GenreDetail", StandardColumns, StandardWeights, StandardMinimums);

    /// <summary>All track tables, so navigation and layout changes can reach each one.</summary>
    public IEnumerable<TrackTableState> TrackTables => new[]
    {
        SongsTable, AlbumTable, RecentPlayTable, RecentAddTable, MostPlayedTable,
        FavoritesTable, ArtistDetailTable, FolderDetailTable, AlbumMusicTable,
        PlaylistDetailTable, GenreDetailTable,
    };

    // ---- resize gesture, shared by every table -------------------------------------

    /// <summary>
    /// Reads and writes one table's column widths, whichever storage that table uses:
    /// the shared <see cref="TrackTableState"/> or the legacy dependency properties the
    /// two hand-written tables still bind their markup to. The resize gesture goes
    /// through this so a drag behaves the same on every table.
    /// </summary>
    private sealed class ColumnAccess
    {
        public required TrackTableState State { get; init; }

        /// <summary>The window that owns the legacy width properties.</summary>
        public required MainWindow Owner { get; init; }

        /// <summary>Legacy width properties: text columns first, the options cell last.</summary>
        public DependencyProperty[]? LegacyProps { get; init; }

        public int TextCount => State.ColumnCount;

        public GridLength Get(int index)
            => LegacyProps is null
                ? State.Widths[index]
                : (GridLength)Owner.GetValue(LegacyProps[index]);

        public void Set(int index, GridLength value)
        {
            if (LegacyProps is null) { State.SetWidth(index, value); return; }
            double min = State.Minimums[Math.Min(index, State.Minimums.Length - 1)];
            Owner.SetValue(LegacyProps[index], new GridLength(Math.Max(min, value.Value), value.GridUnitType));
        }

        public bool IsHidden(int index) => Get(index).Value <= 0;
    }

    /// <summary>The width access for a table key, whichever storage that table uses.</summary>
    private ColumnAccess? AccessFor(string key) => key switch
    {
        "Songs" => new ColumnAccess { State = SongsTable, LegacyProps = SongsColProps, Owner = this },
        "Album" => new ColumnAccess { State = AlbumTable, LegacyProps = AlbumColProps, Owner = this },
        _ => TrackTables.FirstOrDefault(t => t.Key == key) is { } state
                ? new ColumnAccess { State = state, Owner = this }
                : null,
    };

    private ColumnAccess? _dragAccess;
    private int _dragLeft = -1;
    private int _dragRight = -1;
    private double _dragOrigLeft;
    private double _dragOrigRight;
    private double _dragMinLeft;
    private double _dragMinRight;
    private double _dragOriginX;

    /// <summary>
    /// True while a resize gesture is in flight. Freezing the column widths changes
    /// the list's layout, which reports a scroll change, which used to drop the
    /// column selection - the handle under the cursor then collapsed, the mouse
    /// capture was lost and the drag died after a single step.
    /// </summary>
    private bool _isResizing;

    /// <summary>
    /// Pulling a boundary trades width between the two columns meeting there, so the
    /// table keeps its overall width and only the grabbed column changes. Star columns
    /// are frozen to pixels on the first drag, otherwise they would silently re-balance.
    /// The pair is resolved from laid-out geometry rather than a fixed index, because a
    /// hidden column is 0px wide and its handle would otherwise drag it back into view.
    /// </summary>
    private void TableHeader_DragStarted(object? sender, ColumnDragEventArgs e)
    {
        if (sender is TrackTableHeader header)
            BeginResize(new ColumnAccess { State = e.Table, Owner = this }, null, header.HeaderGrid);
    }

    private void TableHeader_DragDelta(object? sender, ColumnDragEventArgs e) => ContinueSharedDrag(0);

    private void TableHeader_DragCompleted(object? sender, ColumnDragEventArgs e) => EndSharedDrag();

    /// <summary>
    /// Resolves the two columns meeting at the grabbed boundary and freezes them to
    /// pixels so star weights stop re-balancing mid-drag.
    /// </summary>
    private void BeginResize(ColumnAccess access, Thumb? thumb = null, Grid? explicitGrid = null)
    {
        _dragAccess = null;
        _dragLeft = -1;
        _dragRight = -1;

        var grid = explicitGrid
                   ?? (thumb is not null ? FindAncestorGrid(thumb) : null)
                   ?? FindAncestorGrid(this.FindName(access.State.Key + "HeaderGrid") as FrameworkElement);
        if (grid is null) return;

        var defs = grid.ColumnDefinitions;
        if (defs.Count < 2) return;

        double handleX = Mouse.GetPosition(grid).X;
        int edge = -1;
        double best = double.MaxValue;
        for (int i = 0; i < defs.Count; i++)
        {
            if (defs[i].ActualWidth <= 0) continue;          // hidden column
            double distance = Math.Abs(defs[i].Offset - handleX);
            if (distance < best) { best = distance; edge = i; }
        }
        if (edge < 0) return;

        int leftColumn = -1;
        for (int i = edge - 1; i >= 0; i--)
            if (defs[i].ActualWidth > 0) { leftColumn = i; break; }
        if (leftColumn < 0) return;

        // Column 0 is the art cell and the last is the fixed "..." cell; neither trades.
        int left = leftColumn - 1;
        int right = edge - 1;
        if (left < 0 || right >= access.TextCount) return;

        // From here on the gesture owns the column selection: freezing the widths
        // re-lays the list out, and that must not take the handle away mid-drag.
        _isResizing = true;

        for (int i = 0; i < access.TextCount; i++)
            if (!access.Get(i).IsAbsolute)
                access.Set(i, new GridLength(defs[i + 1].ActualWidth));

        _dragAccess = access;
        _dragLeft = left;
        _dragRight = right;
        _dragOrigLeft = defs[leftColumn].ActualWidth;
        _dragOrigRight = defs[edge].ActualWidth;
        _dragMinLeft = access.State.Minimums[left];
        _dragMinRight = access.State.Minimums[right];

        var native = new NativePOINT();
        GetCursorPos(ref native);
        _dragOriginX = native.X;
    }

    private void TableHeader_ColumnToggled(object? sender, ColumnIndexEventArgs e)
    {
        if (sender is TrackTableHeader { Table: { } table }) table.ToggleActive(e.ColumnIndex);
    }

    private void TableHeader_HoverChanged(object? sender, ColumnIndexEventArgs e)
    {
        if (sender is TrackTableHeader { Table: { } table }) table.Hover = e.ColumnIndex;
    }

    private void TableHeader_ColumnHideRequested(object? sender, ColumnIndexEventArgs e)
    {
        if (sender is TrackTableHeader { Table: { } table })
        {
            table.HideColumn(e.ColumnIndex);
            // Hiding frees width; the remaining columns take it back up.
            foreach (var st in _sharedSyncs)
                if (ReferenceEquals(st.Access?.State, table)) st.NeedsRefit = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateTracksScrollbarInset));
        }
    }

    // ---- shared columns menu (the trailing "..." on every converted table) -------

    private Popup? _columnsMenu;
    private TrackTableState? _columnsMenuTable;

    /// <summary>
    /// Builds one popup that adapts to whichever table asked for it: a checkbox per
    /// text column, ticking shows and unticking hides - same contract as the legacy
    /// Songs/Album menus, driven entirely from the table state.
    /// </summary>
    private void TableHeader_ColumnsMenuRequested(object? sender, EventArgs e)
    {
        if (sender is not TrackTableHeader header || header.Table is not { } table) return;

        if (_columnsMenu is null)
        {
            _columnsMenu = new Popup
            {
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
            };
            _columnsMenu.Closed += (_, _) => _columnsMenuTable = null;
        }

        _columnsMenuTable = table;
        _columnsMenu.PlacementTarget = (FrameworkElement?)header.ColumnsButton ?? header;
        _columnsMenu.Child = BuildColumnsMenuContent(table);

        _columnsMenu.IsOpen = false;
        _columnsMenu.IsOpen = true;
    }

    private UIElement BuildColumnsMenuContent(TrackTableState table)
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Columns",
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 2, 0, 8),
        });

        for (int i = 0; i < table.ColumnCount; i++)
        {
            int index = i;                       // capture for the closure
            var box = new CheckBox
            {
                Content = table.Labels[i],
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                FontSize = 13,
                Height = 32,
                Cursor = Cursors.Hand,
                IsChecked = !table.IsHidden(i),
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Template = BuildColumnsCheckBoxTemplate(),
            };
            box.Checked += (_, _) => table.ShowColumn(index);
            box.Unchecked += (_, _) => table.HideColumn(index);
            panel.Children.Add(box);
        }

        var restore = new Button
        {
            Content = "Restore all",
            Foreground = new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            Height = 28,
            Margin = new Thickness(6, 6, 6, 2),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Template = BuildRestoreButtonTemplate(),
        };
        restore.Click += (_, _) => { table.RestoreAll(); SyncColumnsMenuChecks(table); };
        panel.Children.Add(restore);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x22, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 8, 6, 8),
            MinWidth = 184,
            Child = panel,
        };
    }

    /// <summary>Re-ticks the boxes after "Restore all" so the menu reflects the state.</summary>
    private void SyncColumnsMenuChecks(TrackTableState table)
    {
        if (_columnsMenu?.Child is not Border { Child: StackPanel panel }) return;
        int child = 1;                              // skip the "Columns" title
        for (int i = 0; i < table.ColumnCount && child < panel.Children.Count; i++, child++)
            if (panel.Children[child] is CheckBox box)
                box.IsChecked = !table.IsHidden(i);
    }

    private static ControlTemplate BuildColumnsCheckBoxTemplate()
    {
        var template = new ControlTemplate(typeof(CheckBox));

        var row = new FrameworkElementFactory(typeof(Border), "Row");
        row.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        row.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        row.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));

        var grid = new FrameworkElementFactory(typeof(Grid));
        var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(26));
        var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        grid.AppendChild(col0);
        grid.AppendChild(col1);

        var box = new FrameworkElementFactory(typeof(Border), "Box");
        box.SetValue(Border.WidthProperty, 16.0);
        box.SetValue(Border.HeightProperty, 16.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
        box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(Grid.ColumnProperty, 0);

        var tick = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "Tick");
        tick.SetValue(System.Windows.Shapes.Path.DataProperty, Application.Current?.TryFindResource("Icon.Check") as Geometry);
        tick.SetValue(System.Windows.Shapes.Path.FillProperty, new SolidColorBrush(Color.FromArgb(0xFF, 0x15, 0x18, 0x1F)));
        tick.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
        tick.SetValue(System.Windows.Shapes.Path.WidthProperty, 9.0);
        tick.SetValue(System.Windows.Shapes.Path.HeightProperty, 9.0);
        tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        tick.SetValue(UIElement.OpacityProperty, 0.0);
        box.AppendChild(tick);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(Grid.ColumnProperty, 1);

        grid.AppendChild(box);
        grid.AppendChild(presenter);
        row.AppendChild(grid);
        template.VisualTree = row;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), "Row"));

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xE6, 0xEA, 0xF2, 0xFF)), "Box"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0xE6, 0xEA, 0xF2, 0xFF)), "Box"));
        checkedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Tick"));

        template.Triggers.Add(hover);
        template.Triggers.Add(checkedTrigger);
        template.Seal();
        return template;
    }

    private static ControlTemplate BuildRestoreButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), "Bd"));
        template.Triggers.Add(hover);
        template.Seal();
        return template;
    }

    // ---- legacy adapters -----------------------------------------------------------
    // The two hand-written headers that predate the shared control still raise WPF's
    // Thumb drag events directly. These keep that markup working by handing the gesture
    // to the shared state, so both paths resize through exactly the same code.

    private void ColumnHandle_DragStarted(object sender, DragStartedEventArgs e)
        => StartSharedDrag(sender, e.OriginalSource as DependencyObject);

    private void ColumnHandle_DragDelta(object sender, DragDeltaEventArgs e)
        => ContinueSharedDrag(e.HorizontalChange);

    private void ColumnHandle_DragCompleted(object sender, DragCompletedEventArgs e)
        => EndSharedDrag();

    private void StartSharedDrag(object sender, DependencyObject? source)
    {
        for (DependencyObject? node = source; node is not null;)
        {
            if (node is Thumb { Tag: string tag } && tag.Contains(':'))
            {
                var key = tag.Split(':')[0].Replace("HeaderGrid", "");
                var access = AccessFor(key);
                if (access is not null) { BeginResize(access, node as Thumb); return; }
            }
            node = VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent;
        }
    }


    private void ContinueSharedDrag(double fallback)
    {
        if (_dragAccess is null) return;
        try
        {
            var native = new NativePOINT();
            bool got = GetCursorPos(ref native);
            double offset = got ? native.X - _dragOriginX : fallback;

            double pair = _dragOrigLeft + _dragOrigRight;
            double upper = Math.Max(_dragMinLeft, pair - _dragMinRight);
            double nextLeft = Math.Min(upper, Math.Max(_dragMinLeft, _dragOrigLeft + offset));
            _dragAccess.Set(_dragLeft, new GridLength(nextLeft));
            _dragAccess.Set(_dragRight, new GridLength(pair - nextLeft));
        }
        catch (Exception ex)
        {
            LogHelper.LogError("Table drag failed", ex);
            EndSharedDrag();
        }
    }

    private void EndSharedDrag()
    {
        _dragAccess = null;
        _isResizing = false;
        _dragLeft = -1;
        _dragRight = -1;
    }



    }
}
