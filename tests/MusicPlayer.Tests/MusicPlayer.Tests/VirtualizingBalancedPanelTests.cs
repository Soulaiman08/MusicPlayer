using MusicPlayer.Views;

namespace MusicPlayer.Tests;

public class VirtualizingBalancedPanelTests
{
    [Fact]
    public void CalculateVisibleRange_UsesSafeValuesForInvalidViewportInputs()
    {
        var range = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount: 100,
            cols: 4,
            offset: double.NaN,
            viewportHeight: double.PositiveInfinity,
            rowHeight: double.NaN,
            gap: double.NaN);

        Assert.Equal(0, range.Offset);
        Assert.Equal(1, range.VisibleRows);
        Assert.Equal(0, range.FirstIndex);
        Assert.InRange(range.LastIndex, range.FirstIndex, 99);
    }

    [Fact]
    public void CalculateVisibleRange_ClampsOffsetAndIndexesToItemBounds()
    {
        var range = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount: 100,
            cols: 4,
            offset: double.MaxValue,
            viewportHeight: 200,
            rowHeight: 200,
            gap: 0);

        Assert.Equal(4800, range.Offset);
        Assert.Equal(88, range.FirstIndex);
        Assert.Equal(99, range.LastIndex);
    }

    [Fact]
    public void CalculateVisibleRange_SaturatesExtremeRowCountsWithoutOverflow()
    {
        var range = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount: int.MaxValue,
            cols: 1,
            offset: double.MaxValue,
            viewportHeight: double.MaxValue,
            rowHeight: double.Epsilon,
            gap: 0);

        Assert.InRange(range.VisibleRows, 1, int.MaxValue);
        Assert.Equal(0, range.FirstIndex);
        Assert.Equal(int.MaxValue - 1, range.LastIndex);
        Assert.Equal(0, range.Offset);
    }

    [Fact]
    public void ResolveViewportHeight_UsesCurrentScrollViewerSizeWhenMeasureHeightIsUnbounded()
    {
        double height = VirtualizingBalancedPanel.ResolveViewportHeight(
            availableHeight: double.PositiveInfinity,
            scrollViewerHeight: 760,
            verticalPadding: 30);

        Assert.Equal(730, height);
    }

    [Fact]
    public void CalculateVisibleRange_AdvancesAcrossTheListAndReachesTheLastItem()
    {
        const int itemCount = 1000;
        const int columns = 4;
        const double viewportHeight = 600;
        const double rowHeight = 200;
        double extentHeight = (Math.Ceiling((double)itemCount / columns) - 1) * rowHeight + rowHeight;
        var firstRange = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount, columns, 0, viewportHeight, rowHeight, gap: 0);
        var middleRange = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount, columns, 6000, viewportHeight, rowHeight, gap: 0);
        var lastRange = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount, columns, double.MaxValue, viewportHeight, rowHeight, gap: 0);

        Assert.True(middleRange.FirstIndex > firstRange.FirstIndex);
        Assert.True(middleRange.LastIndex > firstRange.LastIndex);
        Assert.True(lastRange.FirstIndex > middleRange.FirstIndex);
        Assert.Equal(itemCount - 1, lastRange.LastIndex);
        Assert.Equal(extentHeight - viewportHeight, lastRange.Offset);
        Assert.InRange(firstRange.LastIndex, 0, itemCount - 1);
        Assert.InRange(middleRange.LastIndex, middleRange.FirstIndex, itemCount - 1);
        Assert.InRange(lastRange.LastIndex, lastRange.FirstIndex, itemCount - 1);
        Assert.True(lastRange.LastIndex - lastRange.FirstIndex + 1 < itemCount);
    }

    [Fact]
    public void ResolveGenerationOffset_UsesOuterScrollViewerOffsetWhenPanelIsNotScrollClient()
    {
        double offset = VirtualizingBalancedPanel.ResolveGenerationOffset(
            usesScrollInfo: false,
            panelOffset: 0,
            scrollViewerOffset: 2400);
        var range = VirtualizingBalancedPanel.CalculateVisibleRange(
            itemCount: 1000,
            cols: 4,
            offset,
            viewportHeight: 600,
            rowHeight: 200,
            gap: 0);

        Assert.Equal(2400, offset);
        Assert.Equal(40, range.FirstIndex);
        Assert.True(range.LastIndex > range.FirstIndex);
        Assert.InRange(range.LastIndex, range.FirstIndex, 999);
    }
}
