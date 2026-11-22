using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicPlayer.Diagnostics;

/// <summary>
/// Diagnostic-only Grid used for ContentGrid so the sidebar probe can count the
/// real MeasureOverride / ArrangeOverride / OnRender passes instead of inferring
/// them from LayoutUpdated. The overrides are inert unless the sidebar probe is
/// active; the type itself is otherwise a normal Grid.
/// </summary>
public sealed class InstrumentedGrid : Grid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!SidebarPerfProbe.IsMotionActive)
            return base.MeasureOverride(availableSize);

        long start = Stopwatch.GetTimestamp();
        Size result = base.MeasureOverride(availableSize);
        SidebarPerfProbe.RecordContentMeasure(Stopwatch.GetTimestamp() - start);
        return result;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!SidebarPerfProbe.IsMotionActive)
            return base.ArrangeOverride(finalSize);

        long start = Stopwatch.GetTimestamp();
        Size result = base.ArrangeOverride(finalSize);
        SidebarPerfProbe.RecordContentArrange(Stopwatch.GetTimestamp() - start);
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!SidebarPerfProbe.IsMotionActive)
        {
            base.OnRender(dc);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        base.OnRender(dc);
        SidebarPerfProbe.RecordContentRender(Stopwatch.GetTimestamp() - start);
    }
}
