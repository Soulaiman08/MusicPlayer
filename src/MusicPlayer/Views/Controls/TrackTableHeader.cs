using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MusicPlayer.Views.Controls;

namespace MusicPlayer.Views.Controls;

/// <summary>
/// The heading row shared by every track table. It builds its columns, separators, hover
/// cells, hide buttons and drag handles from a <see cref="TrackTableState"/>, so a page
/// gets identical table chrome by naming a state instead of repeating the markup.
/// </summary>
public sealed class TrackTableHeader : UserControl
{
    public static readonly DependencyProperty TableProperty =
        DependencyProperty.Register(nameof(Table), typeof(TrackTableState), typeof(TrackTableHeader),
            new PropertyMetadata(null, OnTableChanged));

    /// <summary>
    /// Left padding of every heading text, in its own column. Defaults to the historic
    /// 18px; pages whose row text uses a 12px inset opt in to 12 so headings and row
    /// text share one vertical groove. Purely additive: the default keeps every
    /// existing table pixel-identical.
    /// </summary>
    public static readonly DependencyProperty HeaderPaddingLeftProperty =
        DependencyProperty.Register(nameof(HeaderPaddingLeft), typeof(double), typeof(TrackTableHeader),
            new PropertyMetadata(18.0));

    /// <summary>
    /// Draws the footer-to-footer vertical rules between columns. Table chrome from
    /// the original design; pages that show the lighter, rule-less list look turn
    /// this off. Default true keeps every existing table unchanged.
    /// </summary>
    public static readonly DependencyProperty ShowColumnRulesProperty =
        DependencyProperty.Register(nameof(ShowColumnRules), typeof(bool), typeof(TrackTableHeader),
            new PropertyMetadata(true));

    public double HeaderPaddingLeft
    {
        get => (double)GetValue(HeaderPaddingLeftProperty);
        set => SetValue(HeaderPaddingLeftProperty, value);
    }

    public bool ShowColumnRules
    {
        get => (bool)GetValue(ShowColumnRulesProperty);
        set => SetValue(ShowColumnRulesProperty, value);
    }

    private static readonly Brush _lineBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly Brush _hoverFill = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
    private static readonly Brush _closeFill = new SolidColorBrush(Color.FromArgb(0xC4, 0xFF, 0xFF, 0xFF));

    private readonly Grid _grid = new() { Height = 38, SnapsToDevicePixels = true, UseLayoutRounding = true };
    private readonly List<Grid> _cells = new();
    private readonly List<Thumb> _handles = new();
    private readonly List<int> _handleColumns = new();
    private bool _hooked;

    public TrackTableState? Table
    {
        get => (TrackTableState?)GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    /// <summary>Lets the window measure the laid-out columns when a handle is dragged.</summary>
    public Grid HeaderGrid => _grid;

    /// <summary>The trailing "..." button, so a popup can anchor beneath it.</summary>
    public Button? ColumnsButton { get; private set; }

    /// <summary>Raised with the column index when a heading is clicked.</summary>
    public event EventHandler<ColumnIndexEventArgs>? ColumnToggled;

    /// <summary>Raised with the column index, or -1 when the pointer leaves the header.</summary>
    public event EventHandler<ColumnIndexEventArgs>? HoverChanged;

    /// <summary>Raised with the column index when a heading's hide button is pressed.</summary>
    public event EventHandler<ColumnIndexEventArgs>? ColumnHideRequested;

    /// <summary>Raised when the trailing "..." button is pressed.</summary>
    public event EventHandler? ColumnsMenuRequested;

    /// <summary>Forwards a handle's drag gesture so the window can drive the resize.</summary>
    public event EventHandler<ColumnDragEventArgs>? DragStarted;
    public event EventHandler<ColumnDragEventArgs>? DragDelta;
    public event EventHandler<ColumnDragEventArgs>? DragCompleted;

    public TrackTableHeader()
    {
        Content = _grid;
    }

    private static void OnTableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (TrackTableHeader)d;
        TrackTable.SetState(header, header.Table);
        header.Build();
    }

    private void Build()
    {
        var old = Table;
        if (_hooked && old is not null)
        {
            old.PropertyChanged -= OnStatePropertyChanged;
            old.Widths.CollectionChanged -= OnWidthsChanged;
            old.Hidden.CollectionChanged -= OnHiddenChanged;
        }
        _hooked = false;

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _cells.Clear();
        _handles.Clear();
        _handleColumns.Clear();

        var table = Table;
        if (table is null) return;

        table.PropertyChanged += OnStatePropertyChanged;
        table.Widths.CollectionChanged += OnWidthsChanged;
        table.Hidden.CollectionChanged += OnHiddenChanged;
        _hooked = true;

        int text = table.ColumnCount;
        // The art cell matches the rows: the artwork fills it completely, with no
        // margin, so the column reads as one flush strip of covers.
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        for (int i = 0; i < text; i++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(table.Weights[i], GridUnitType.Star), MinWidth = 0 });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Pixel), MinWidth = 0 });

        AddArt();
        for (int i = 0; i < text; i++)
            AddTextColumn(table, i);
        AddOptionsCell(table, text);
        // Handles sit on the left edge of each text column starting at column 1
        // (Artists onward). The boundary between art and Title has no handle because
        // the art column is fixed.
        for (int i = 1; i < text; i++)
            AddHandle(table, i, i + 1);

        ApplyWidths();
        ApplyState();
    }

    private void AddArt()
    {
        var art = new Path
        {
            Data = Application.Current?.TryFindResource("Icon.Album") as Geometry,
            Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Stretch = Stretch.Uniform,
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(art, 0);
        _grid.Children.Add(art);
    }

    private void AddSeparator(int column)
    {
        if (!ShowColumnRules) return;
        var separator = new Border
        {
            BorderBrush = _lineBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, -4, 0, -4),
            IsHitTestVisible = false,
        };
        Grid.SetColumn(separator, column);
        _grid.Children.Add(separator);
    }

    private void AddTextColumn(TrackTableState table, int index)
    {
        // Every column is separated from the one before it - the art cell included,
        // so the table reads as one continuous set of vertical rules from the
        // header down through the rows.
        AddSeparator(index + 1);

        var cell = new Grid { Tag = index, Cursor = Cursors.Hand, Margin = new Thickness(0, -4, 0, -1) };
        Grid.SetColumn(cell, index + 1);
        cell.MouseLeftButtonUp += OnCellClick;
        cell.MouseEnter += OnCellEnter;
        cell.MouseLeave += OnCellLeave;

        cell.Children.Add(new TextBlock
        {
            Tag = index,
            Text = table.Labels[index],
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(HeaderPaddingLeft, 0, 46, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "Click to resize this column",
        });

        var hide = CreateHideButton(table, index);
        hide.Click += (_, _) => ColumnHideRequested?.Invoke(this, new ColumnIndexEventArgs(index));
        cell.Children.Add(hide);

        _grid.Children.Add(cell);
        _cells.Add(cell);
    }

    private Button CreateHideButton(TrackTableState table, int index)
    {
        var badge = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Style = BuildBadgeStyle();
        badge.Child = new Path
        {
            Data = Application.Current?.TryFindResource("Icon.Close") as Geometry,
            Fill = _closeFill,
            Stretch = Stretch.Uniform,
            Width = 9,
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Tag = table.ColumnKey(index),
            ToolTip = $"Hide the {table.Labels[index]} column",
            Width = 26,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Content = badge,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        button.SetBinding(UIElement.VisibilityProperty, TrackBinding.Active(table, index));
        return button;
    }

    /// <summary>Quiet at rest; the disc only surfaces while the button is hovered.</summary>
    private static Style BuildBadgeStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));

        var hover = new Trigger { Property = FrameworkElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF))));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))));
        style.Triggers.Add(hover);
        style.Seal();
        return style;
    }

    private void AddOptionsCell(TrackTableState table, int optionsColumn)
    {
        AddSeparator(optionsColumn + 1);

        var button = new Button
        {
            ToolTip = "Show or hide columns",
            Width = 32,
            Height = 32,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new Path
            {
                Data = Geometry.Parse("M3.5,4.5 H8 V19.5 H3.5 Z M10,4.5 H14.5 V19.5 H10 Z M16.5,4.5 H21 V19.5 H16.5 Z"),
                Fill = new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF)),
                Stretch = Stretch.Uniform,
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) => ColumnsMenuRequested?.Invoke(this, EventArgs.Empty);
        ColumnsButton = button;
        Grid.SetColumn(button, optionsColumn + 1);
        _grid.Children.Add(button);
    }

    private void AddHandle(TrackTableState table, int index, int column)
    {
        var thumb = new Thumb
        {
            Tag = table.Key + ":1:" + index,
            Width = 28,
            Height = 28,
            Cursor = Cursors.SizeWE,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-14, 0, 0, -14),
            ToolTip = "Drag to resize",
            Template = BuildHandleTemplate(),
        };
        Panel.SetZIndex(thumb, 90);
        Grid.SetColumn(thumb, column);
        thumb.DragStarted += (_, _) => DragStarted?.Invoke(this, new ColumnDragEventArgs(table, index));
        thumb.DragDelta += (_, _) => DragDelta?.Invoke(this, new ColumnDragEventArgs(table, index));
        thumb.DragCompleted += (_, _) => DragCompleted?.Invoke(this, new ColumnDragEventArgs(table, index));
        _grid.Children.Add(thumb);
        _handles.Add(thumb);
        _handleColumns.Add(index);
    }

    private static ControlTemplate BuildHandleTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));

        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var bubble = new FrameworkElementFactory(typeof(Border));
        bubble.SetValue(Border.WidthProperty, 28.0);
        bubble.SetValue(Border.HeightProperty, 28.0);
        bubble.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        bubble.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x1F, 0x1F)));
        bubble.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x59, 0x59, 0x59, 0x59)));
        bubble.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bubble.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bubble.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var arrows = new FrameworkElementFactory(typeof(Path));
        arrows.SetValue(Path.DataProperty, Geometry.Parse("M0,3.5 L3.5,0 L3.5,7 Z M13,3.5 L9.5,0 L9.5,7 Z"));
        arrows.SetValue(Path.WidthProperty, 13.0);
        arrows.SetValue(Path.HeightProperty, 7.0);
        arrows.SetValue(Path.FillProperty, new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0xA8, 0xA8)));
        arrows.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        arrows.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        bubble.AppendChild(arrows);
        root.AppendChild(bubble);
        template.VisualTree = root;
        template.Seal();
        return template;
    }

    private void OnCellClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
            ColumnToggled?.Invoke(this, new ColumnIndexEventArgs(index));
        e.Handled = true;
    }

    private void OnCellEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
            HoverChanged?.Invoke(this, new ColumnIndexEventArgs(index));
    }

    private void OnCellLeave(object sender, MouseEventArgs e) => HoverChanged?.Invoke(this, new ColumnIndexEventArgs(-1));

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();
    private void OnWidthsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyWidths();
        // A width of zero means the column is hidden, which changes which handles
        // are worth showing.
        ApplyState();
    }
    private void OnHiddenChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyState();

    private void ApplyWidths()
    {
        var table = Table;
        if (table is null) return;
        for (int i = 0; i < table.ColumnCount && i + 1 < _grid.ColumnDefinitions.Count; i++)
            _grid.ColumnDefinitions[i + 1].Width = table.Widths[i];
    }

    /// <summary>
    /// The selected column is bracketed by arrows: one on its left edge and one on
    /// the left edge of the next visible column after it, which is where its right
    /// edge lands. A hidden column is skipped, because a 0px cell has no edge to
    /// anchor a handle to.
    /// </summary>
    private void ApplyState()
    {
        var table = Table;
        if (table is null) return;

        for (int k = 0; k < _handles.Count; k++)
        {
            int handle = _handleColumns[k];
            bool visible = false;

            if (table.Active >= 0 && !table.IsHidden(handle))
            {
                if (handle == table.Active)
                {
                    visible = true;                          // the column's own left edge
                }
                else
                {
                    // The column's right edge: the next column that is still visible.
                    for (int i = table.Active + 1; i < table.ColumnCount; i++)
                    {
                        if (table.IsHidden(i)) continue;
                        visible = handle == i;
                        break;
                    }
                }
            }

            _handles[k].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        for (int i = 0; i < _cells.Count; i++)
            _cells[i].Background = table.Hover == i ? _hoverFill : Brushes.Transparent;
    }

    /// <summary>Reports whether a point sits over a handle, so a press there is not a deselect.</summary>
    public bool OwnsHandle(object? source)
    {
        for (DependencyObject? node = source as DependencyObject; node is not null;)
        {
            if (node is Thumb) return true;
            node = VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent;
        }
        return false;
    }
}

/// <summary>Reports which column a header gesture belongs to.</summary>
public sealed class ColumnIndexEventArgs : EventArgs
{
    public ColumnIndexEventArgs(int columnIndex) => ColumnIndex = columnIndex;

    public int ColumnIndex { get; }
}

/// <summary>Carries which table and column a resize gesture belongs to.</summary>
public sealed class ColumnDragEventArgs : EventArgs
{
    public ColumnDragEventArgs(TrackTableState table, int columnIndex)
    {
        Table = table;
        ColumnIndex = columnIndex;
    }

    public TrackTableState Table { get; }
    public int ColumnIndex { get; }
}

internal static class TrackBinding
{
    /// <summary>Visible only while the table's Active column equals <paramref name="index"/>.</summary>
    public static System.Windows.Data.Binding Active(TrackTableState table, int index)
        => new(nameof(TrackTableState.Active))
        {
            Source = table,
            Converter = ActiveColumnToVisibility.Instance,
            ConverterParameter = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    /// <summary>Visible only while the observed collection is not empty.</summary>
    public static System.Windows.Data.Binding NonEmpty(System.Collections.ObjectModel.ObservableCollection<string> collection)
        => new("Count") { Source = collection, Converter = CountToVisibility.Instance };
}

internal sealed class ActiveColumnToVisibility : IValueConverter
{
    public static readonly ActiveColumnToVisibility Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool match = parameter is not null
            && value is int active
            && int.TryParse(parameter.ToString(), out int wanted)
            && active == wanted;
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class CountToVisibility : IValueConverter
{
    public static readonly CountToVisibility Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}