using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MusicPlayer.Views;

/// <summary>
/// A high-performance VirtualizingWrapPanel implementing grid virtualization for items
/// with uniform card sizes (Albums and Artists grids).
/// Correctly utilizes IItemContainerGenerator / IRecyclingItemContainerGenerator,
/// supports IScrollInfo, dynamic item size detection, and handles ItemsSource mutations cleanly.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    // Dependency properties for explicit item dimensions (optional override)
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    // Scroll state
    private bool _canVerticallyScroll;
    private bool _canHorizontallyScroll;
    private ScrollViewer? _scrollOwner;
    private ScrollViewer? _ancestorScrollViewer;
    private Point _scrollOffset;
    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);

    // Grid layout metrics
    private int _columns = 1;
    private int _itemsCount = 0;
    private Size _resolvedItemSize = new(158, 195); // Dynamic sample fallback
    private bool _hasMeasuredSample = false;

    // Buffer rows for smooth scrolling without pop-in (1 row before, 1 row after)
    private const int BufferRows = 1;

    public VirtualizingWrapPanel()
    {
        _canVerticallyScroll = true;
    }

    #region Measure & Arrange

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        _itemsCount = itemsControl?.Items.Count ?? 0;

        if (_itemsCount == 0)
        {
            CleanupAllContainers();
            UpdateScrollInfo(availableSize, new Size(0, 0));
            return new Size(0, 0);
        }

        // Hook ancestor ScrollViewer if the panel is hosted inside an external ScrollViewer (infinite available height)
        EnsureAncestorScrollViewer();

        // 1. Resolve item dimensions dynamically from content if not explicitly specified
        ResolveItemDimensions(availableSize);

        // 2. Compute columns and rows
        double panelWidth = double.IsPositiveInfinity(availableSize.Width)
            ? (ActualWidth > 0 ? ActualWidth : (_ancestorScrollViewer?.ActualWidth ?? 800))
            : availableSize.Width;

        _columns = Math.Max(1, (int)Math.Floor(panelWidth / Math.Max(1, _resolvedItemSize.Width)));
        int totalRows = (_itemsCount + _columns - 1) / _columns;
        double extentHeight = totalRows * _resolvedItemSize.Height;
        double extentWidth = _columns * _resolvedItemSize.Width;

        // 3. Determine viewport and current scroll offset
        double currentVerticalOffset;
        double currentViewportHeight;

        if (_scrollOwner != null && !double.IsPositiveInfinity(availableSize.Height))
        {
            currentVerticalOffset = _scrollOffset.Y;
            currentViewportHeight = availableSize.Height;
        }
        else if (_ancestorScrollViewer != null)
        {
            currentVerticalOffset = _ancestorScrollViewer.VerticalOffset;
            currentViewportHeight = _ancestorScrollViewer.ActualHeight > 0 ? _ancestorScrollViewer.ActualHeight : 600;
        }
        else
        {
            currentVerticalOffset = _scrollOffset.Y;
            currentViewportHeight = double.IsPositiveInfinity(availableSize.Height) ? 800 : availableSize.Height;
        }

        // Clamp offset within bounds
        currentVerticalOffset = Math.Max(0, Math.Min(currentVerticalOffset, Math.Max(0, extentHeight - currentViewportHeight)));

        // 4. Calculate visible item range with buffer rows
        int firstVisibleRow = Math.Max(0, (int)Math.Floor(currentVerticalOffset / _resolvedItemSize.Height) - BufferRows);
        int lastVisibleRow = Math.Min(totalRows - 1, (int)Math.Ceiling((currentVerticalOffset + currentViewportHeight) / _resolvedItemSize.Height) + BufferRows);

        int firstVisibleIndex = firstVisibleRow * _columns;
        int lastVisibleIndex = Math.Min(_itemsCount - 1, ((lastVisibleRow + 1) * _columns) - 1);

        // 5. Generate and realize items within the visible range, recycle items outside
        RealizeRange(firstVisibleIndex, lastVisibleIndex);

        // 6. Update Extent and Viewport for IScrollInfo
        Size computedExtent = new Size(panelWidth, extentHeight);
        Size computedViewport = new Size(panelWidth, currentViewportHeight);
        UpdateScrollInfo(computedViewport, computedExtent);

        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? extentWidth : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? extentHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_itemsCount == 0 || InternalChildren.Count == 0)
            return finalSize;

        var generator = ItemContainerGenerator;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0 || itemIndex >= _itemsCount) continue;

            int row = itemIndex / _columns;
            int col = itemIndex % _columns;

            double x = col * _resolvedItemSize.Width;
            double y = row * _resolvedItemSize.Height;

            // If using internal IScrollInfo offset, subtract vertical offset
            if (_scrollOwner != null && _ancestorScrollViewer == null)
            {
                y -= _scrollOffset.Y;
            }

            child.Arrange(new Rect(new Point(x, y), _resolvedItemSize));
        }

        return finalSize;
    }

    #endregion

    #region Container Generation and Recycling (Correct IItemContainerGenerator logic)

    private void RealizeRange(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        if (generator == null) return;

        var recycler = generator as IRecyclingItemContainerGenerator;

        // Step A: Recycle out-of-range containers (traversing backward to preserve indices)
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var genPos = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(genPos);

            if (itemIndex < firstIndex || itemIndex > lastIndex)
            {
                if (recycler != null)
                {
                    recycler.Recycle(genPos, 1);
                }
                else
                {
                    generator.Remove(genPos, 1);
                }
                RemoveInternalChildRange(i, 1);
            }
        }

        if (firstIndex > lastIndex || firstIndex >= _itemsCount)
            return;

        // Step B: Generate missing items within the range [firstIndex, lastIndex]
        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out bool isNewlyRealized);
                if (child == null) break;

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }
                    generator.PrepareItemContainer(child);
                }

                // Measure container with resolved item size
                child.Measure(_resolvedItemSize);
                childIndex++;
            }
        }
    }

    private void CleanupAllContainers()
    {
        if (InternalChildren.Count == 0) return;
        var generator = ItemContainerGenerator;
        if (generator != null)
        {
            var recycler = generator as IRecyclingItemContainerGenerator;
            var pos = new GeneratorPosition(0, 0);
            if (recycler != null)
            {
                recycler.Recycle(pos, InternalChildren.Count);
            }
            else
            {
                generator.Remove(pos, InternalChildren.Count);
            }
        }
        RemoveInternalChildRange(0, InternalChildren.Count);
    }

    private void ResolveItemDimensions(Size availableSize)
    {
        if (!double.IsNaN(ItemWidth) && ItemWidth > 0 &&
            !double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            _resolvedItemSize = new Size(ItemWidth, ItemHeight);
            return;
        }

        // Measure a sample container once to determine exact card layout dimensions
        if (!_hasMeasuredSample && _itemsCount > 0)
        {
            var generator = ItemContainerGenerator;
            if (generator != null)
            {
                GeneratorPosition startPos = generator.GeneratorPositionFromIndex(0);
                using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                {
                    var sample = (UIElement)generator.GenerateNext(out bool isNewlyRealized);
                    if (sample != null)
                    {
                        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        if (sample.DesiredSize.Width > 0 && sample.DesiredSize.Height > 0)
                        {
                            _resolvedItemSize = sample.DesiredSize;
                            _hasMeasuredSample = true;
                        }

                        if (isNewlyRealized)
                        {
                            var recycler = generator as IRecyclingItemContainerGenerator;
                            var pos = new GeneratorPosition(0, 0);
                            if (recycler != null) recycler.Recycle(pos, 1);
                            else generator.Remove(pos, 1);
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region ItemsSource Changes Handling

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                _hasMeasuredSample = false;
                CleanupAllContainers();
                _scrollOffset = new Point(0, 0);
                break;

            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                // Remove the affected child container range so it is freshly re-bound
                if (args.Position.Index >= 0 && args.Position.Index < InternalChildren.Count)
                {
                    RemoveInternalChildRange(args.Position.Index, args.ItemCount);
                }
                break;

            case NotifyCollectionChangedAction.Add:
                // Generated positions will adjust upon next measure
                break;
        }

        InvalidateMeasure();
        _scrollOwner?.InvalidateScrollInfo();
    }

    #endregion

    #region ScrollViewer Detection & Integration

    private void EnsureAncestorScrollViewer()
    {
        if (_ancestorScrollViewer != null) return;

        DependencyObject current = VisualTreeHelper.GetParent(this);
        while (current != null)
        {
            if (current is ScrollViewer sv && sv != _scrollOwner)
            {
                _ancestorScrollViewer = sv;
                _ancestorScrollViewer.ScrollChanged += OnAncestorScrollViewerChanged;
                break;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void OnAncestorScrollViewerChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0)
        {
            InvalidateMeasure();
        }
    }

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        bool changed = Math.Abs(_viewport.Width - viewport.Width) > 0.1 ||
                        Math.Abs(_viewport.Height - viewport.Height) > 0.1 ||
                        Math.Abs(_extent.Width - extent.Width) > 0.1 ||
                        Math.Abs(_extent.Height - extent.Height) > 0.1;

        _viewport = viewport;
        _extent = extent;

        if (changed)
        {
            _scrollOwner?.InvalidateScrollInfo();
        }
    }

    #endregion

    #region IScrollInfo Implementation

    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _scrollOffset.X;
    public double VerticalOffset => _scrollOffset.Y;
    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set => _scrollOwner = value;
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - _resolvedItemSize.Height);
    public void LineDown() => SetVerticalOffset(VerticalOffset + _resolvedItemSize.Height);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (_resolvedItemSize.Height * 0.75));
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (_resolvedItemSize.Height * 0.75));

    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - _resolvedItemSize.Width);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + _resolvedItemSize.Width);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - _resolvedItemSize.Width);
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + _resolvedItemSize.Width);

    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        double clamped = Math.Max(0, Math.Min(offset, maxOffset));

        if (Math.Abs(_scrollOffset.Y - clamped) > 0.1)
        {
            _scrollOffset.Y = clamped;
            InvalidateMeasure();
            _scrollOwner?.InvalidateScrollInfo();
        }
    }

    public void SetHorizontalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        double maxOffset = Math.Max(0, _extent.Width - _viewport.Width);
        double clamped = Math.Max(0, Math.Min(offset, maxOffset));

        if (Math.Abs(_scrollOffset.X - clamped) > 0.1)
        {
            _scrollOffset.X = clamped;
            InvalidateMeasure();
            _scrollOwner?.InvalidateScrollInfo();
        }
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual == null || !IsAncestorOf(visual))
            return Rect.Empty;

        // Traverse visual tree to identify the immediate child container
        UIElement? container = null;
        DependencyObject current = visual;
        while (current != null && current != this)
        {
            if (VisualTreeHelper.GetParent(current) == this)
            {
                container = current as UIElement;
                break;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        if (container == null) return Rect.Empty;

        int childIndex = InternalChildren.IndexOf(container);
        if (childIndex < 0) return Rect.Empty;

        int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
        if (itemIndex < 0 || itemIndex >= _itemsCount) return Rect.Empty;

        BringIndexIntoView(itemIndex);

        int row = itemIndex / _columns;
        int col = itemIndex % _columns;
        double x = col * _resolvedItemSize.Width;
        double y = (row * _resolvedItemSize.Height) - _scrollOffset.Y;

        return new Rect(x, y, _resolvedItemSize.Width, _resolvedItemSize.Height);
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= _itemsCount) return;

        int row = index / _columns;
        double targetTop = row * _resolvedItemSize.Height;
        double targetBottom = targetTop + _resolvedItemSize.Height;

        double currentOffset = _ancestorScrollViewer != null ? _ancestorScrollViewer.VerticalOffset : _scrollOffset.Y;
        double currentHeight = _ancestorScrollViewer != null ? _ancestorScrollViewer.ActualHeight : _viewport.Height;

        if (targetTop < currentOffset)
        {
            if (_ancestorScrollViewer != null) _ancestorScrollViewer.ScrollToVerticalOffset(targetTop);
            else SetVerticalOffset(targetTop);
        }
        else if (targetBottom > currentOffset + currentHeight)
        {
            double newOffset = targetBottom - currentHeight;
            if (_ancestorScrollViewer != null) _ancestorScrollViewer.ScrollToVerticalOffset(newOffset);
            else SetVerticalOffset(newOffset);
        }
    }

    #endregion
}
