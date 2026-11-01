using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Library;

namespace MusicPlayer.Views
{
    public class BalancedItemsPanel : Panel
    {
        public static readonly DependencyProperty DiagnosticKeyProperty =
            DependencyProperty.Register(
                nameof(DiagnosticKey),
                typeof(string),
                typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(string.Empty));

        internal enum LayoutPass
        {
            Measure,
            Arrange
        }

        private sealed class PassTotals
        {
            public long Calls;
            public long ChildrenAtFirstCall = -1;
            public long InclusiveWallTicks;
            public long ExclusiveWallTicks;
            public long InclusiveCpuTicks;
            public long ExclusiveCpuTicks;
            public long InclusiveCpuSamples;
            public long ExclusiveCpuSamples;
            public long NestedCalls;
            public long ChildMeasureCalls;
            public long ChildMeasureWallTicks;
            public long ChildArrangeCalls;
            public long ChildArrangeWallTicks;
        }

        private sealed class PassFrame
        {
            public LayoutPass Pass;
            public long WallStarted;
            public long CpuStarted;
            public bool HasCpuStarted;
            public long NestedWallTicks;
            public long NestedCpuTicks;
            public long NestedCalls;
            public bool NestedCpuComplete = true;
        }

        internal sealed class LayoutDiagnosticProbe
        {
            private BalancedItemsPanel? _owner;
            internal readonly string TargetKey;
            private readonly string _transition;
            private readonly string _state;
            private readonly long _started;
            private int _childrenAtStart = -1;
            private readonly PassTotals _measure = new();
            private readonly PassTotals _arrange = new();
            private readonly List<PassFrame> _frames = new();
            private bool _completed;

            private LayoutDiagnosticProbe(string targetKey, string transition, string state)
            {
                TargetKey = targetKey;
                _transition = transition;
                _state = state;
                _started = Stopwatch.GetTimestamp();
            }

            internal static LayoutDiagnosticProbe Create(string targetKey, string transition, string state) =>
                new(targetKey, transition, state);

            internal bool Attach(BalancedItemsPanel owner)
            {
                if (_completed || _owner != null) return false;
                _owner = owner;
                _childrenAtStart = owner.Children.Count;
                owner._diagnosticProbe = this;
                return true;
            }

            internal LayoutPassMeasurement Begin(LayoutPass pass, int childCount)
            {
                if (_completed) return default;

                var totals = GetTotals(pass);
                if (totals.Calls == 0) totals.ChildrenAtFirstCall = childCount;
                totals.Calls++;

                var frame = new PassFrame
                {
                    Pass = pass,
                    WallStarted = Stopwatch.GetTimestamp(),
                    HasCpuStarted = AlbumArtLoader.TryGetCurrentThreadCpuTicks(out long cpuStarted),
                    CpuStarted = cpuStarted
                };
                int frameIndex = _frames.Count;
                _frames.Add(frame);
                return new LayoutPassMeasurement(this, frameIndex);
            }

            internal string Complete(string endReason, long firstRenderTimestamp)
            {
                if (_completed) return string.Empty;
                _completed = true;
                    if (_owner != null && ReferenceEquals(_owner._diagnosticProbe, this))
                    _owner._diagnosticProbe = null;
                    if (ReferenceEquals(_pendingDiagnosticProbe, this))
                        _pendingDiagnosticProbe = null;

                    string firstRenderMs = firstRenderTimestamp > 0
                        ? $"{(firstRenderTimestamp - _started) * 1000.0 / Stopwatch.Frequency:0.0}"
                        : "not-observed";
                    return
                        $"TAB-PANEL-PERF transition={_transition} state={_state} end={endReason} " +
                        $"firstRenderMs={firstRenderMs} panelAttached={_owner != null} childrenAtStart={_childrenAtStart} " +
                        $"{FormatPass("measure", _measure)} {FormatPass("arrange", _arrange)}";
            }

            private PassTotals GetTotals(LayoutPass pass) =>
                pass == LayoutPass.Measure ? _measure : _arrange;

            private void RecordChildCalls(int frameIndex, long measureCalls, long measureWallTicks, long arrangeCalls, long arrangeWallTicks)
            {
                if (frameIndex != _frames.Count - 1)
                    return;

                PassTotals totals = GetTotals(_frames[frameIndex].Pass);
                totals.ChildMeasureCalls += measureCalls;
                totals.ChildMeasureWallTicks += measureWallTicks;
                totals.ChildArrangeCalls += arrangeCalls;
                totals.ChildArrangeWallTicks += arrangeWallTicks;
            }

            private void End(int frameIndex)
            {
                if (frameIndex != _frames.Count - 1)
                    return;

                PassFrame frame = _frames[frameIndex];
                _frames.RemoveAt(frameIndex);
                long wallElapsed = Stopwatch.GetTimestamp() - frame.WallStarted;
                bool hasCpuEnded = AlbumArtLoader.TryGetCurrentThreadCpuTicks(out long cpuEnded);
                bool hasCpu = frame.HasCpuStarted && hasCpuEnded && cpuEnded >= frame.CpuStarted;
                long cpuElapsed = hasCpu ? cpuEnded - frame.CpuStarted : 0L;
                long exclusiveWall = Math.Max(0, wallElapsed - frame.NestedWallTicks);
                long exclusiveCpu = hasCpu && frame.NestedCpuComplete
                    ? Math.Max(0, cpuElapsed - frame.NestedCpuTicks)
                    : 0L;
                PassTotals totals = GetTotals(frame.Pass);
                totals.InclusiveWallTicks += wallElapsed;
                totals.ExclusiveWallTicks += exclusiveWall;
                totals.NestedCalls += frame.NestedCalls;
                if (hasCpu)
                {
                    totals.InclusiveCpuTicks += cpuElapsed;
                    totals.InclusiveCpuSamples++;
                    if (frame.NestedCpuComplete)
                    {
                        totals.ExclusiveCpuTicks += exclusiveCpu;
                        totals.ExclusiveCpuSamples++;
                    }
                }

                if (_frames.Count == 0) return;

                PassFrame parent = _frames[^1];
                parent.NestedWallTicks += wallElapsed;
                parent.NestedCalls++;
                if (hasCpu)
                    parent.NestedCpuTicks += cpuElapsed;
                else
                    parent.NestedCpuComplete = false;
                if (!frame.NestedCpuComplete)
                    parent.NestedCpuComplete = false;
            }

            private static string FormatPass(string name, PassTotals totals)
            {
                long childCallWallTicks = totals.ChildMeasureWallTicks + totals.ChildArrangeWallTicks;
                long remainingWallTicks = Math.Max(0, totals.InclusiveWallTicks - childCallWallTicks);
                return
                    $"{name}Calls={totals.Calls} {name}ChildrenAtFirstCall={totals.ChildrenAtFirstCall} " +
                    $"{name}WallInclusiveMs={ToMs(totals.InclusiveWallTicks):0.00} " +
                    $"{name}WallExclusiveMs={ToMs(totals.ExclusiveWallTicks):0.00} " +
                    $"{name}ChildMeasureCalls={totals.ChildMeasureCalls} {name}ChildMeasureWallMs={ToMs(totals.ChildMeasureWallTicks):0.00} " +
                    $"{name}ChildArrangeCalls={totals.ChildArrangeCalls} {name}ChildArrangeWallMs={ToMs(totals.ChildArrangeWallTicks):0.00} " +
                    $"{name}OutsideChildCallsWallMs={ToMs(remainingWallTicks):0.00} " +
                    $"{name}UiCpuInclusiveMs={FormatCpu(totals.InclusiveCpuTicks, totals.InclusiveCpuSamples, totals.Calls)} " +
                    $"{name}UiCpuExclusiveMs={FormatCpu(totals.ExclusiveCpuTicks, totals.ExclusiveCpuSamples, totals.Calls)} " +
                    $"{name}NestedCalls={totals.NestedCalls}";
            }

            private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

            private static string FormatCpu(long ticks, long samples, long calls) =>
                calls == 0 || samples != calls
                    ? "partial"
                    : $"{ticks / 10000.0:0.00}";

            internal readonly struct LayoutPassMeasurement : IDisposable
            {
                private readonly LayoutDiagnosticProbe? _probe;
                private readonly int _frameIndex;

                internal LayoutPassMeasurement(LayoutDiagnosticProbe probe, int frameIndex)
                {
                    _probe = probe;
                    _frameIndex = frameIndex;
                }

                internal bool IsEnabled => _probe != null;

                internal void RecordChildCalls(long measureCalls, long measureWallTicks, long arrangeCalls, long arrangeWallTicks) =>
                    _probe?.RecordChildCalls(_frameIndex, measureCalls, measureWallTicks, arrangeCalls, arrangeWallTicks);

                public void Dispose() => _probe?.End(_frameIndex);
            }
        }

        private static LayoutDiagnosticProbe? _pendingDiagnosticProbe;
        private LayoutDiagnosticProbe? _diagnosticProbe;

        public string DiagnosticKey
        {
            get => (string)GetValue(DiagnosticKeyProperty);
            set => SetValue(DiagnosticKeyProperty, value);
        }

        internal static LayoutDiagnosticProbe StartDiagnosticProbe(
            BalancedItemsPanel? panel,
            string key,
            string transition,
            string state)
        {
            var probe = LayoutDiagnosticProbe.Create(key, transition, state);
            if (panel != null)
                probe.Attach(panel);
            else
                _pendingDiagnosticProbe = probe;
            return probe;
        }

        private LayoutDiagnosticProbe? GetDiagnosticProbe()
        {
            if (_diagnosticProbe != null) return _diagnosticProbe;

            var pending = _pendingDiagnosticProbe;
            if (pending != null &&
                string.Equals(DiagnosticKey, pending.TargetKey, StringComparison.Ordinal) &&
                pending.Attach(this))
            {
                _pendingDiagnosticProbe = null;
                return pending;
            }

            return null;
        }

        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register("MinItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty MaxItemWidthProperty =
            DependencyProperty.Register("MaxItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty GapProperty =
            DependencyProperty.Register("Gap", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        private static readonly DependencyPropertyKey ItemWidthPropertyKey =
            DependencyProperty.RegisterReadOnly("ItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemWidthProperty = ItemWidthPropertyKey.DependencyProperty;

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public double MaxItemWidth
        {
            get => (double)GetValue(MaxItemWidthProperty);
            set => SetValue(MaxItemWidthProperty, value);
        }

        public double Gap
        {
            get => (double)GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        public double ItemWidth => (double)GetValue(ItemWidthProperty);

        private double _slot;
        private double _gapH;
        private double _rowHeight;

        private void Solve(double width)
        {
            var w = width > 0 && !double.IsNaN(width) && !double.IsInfinity(width) ? width : Math.Max(1, MinItemWidth);
            var gap = Math.Max(0, Gap);
            var min = Math.Clamp(MinItemWidth, 1d, w);
            var max = Math.Max(min, MaxItemWidth);
            var cols = (int)Math.Floor((w + gap) / (min + gap));
            var slot = Math.Min(max, (w - gap * (cols - 1)) / cols);
            var used = slot * cols + gap * (cols - 1);
            _slot = Math.Max(1, slot);
            _gapH = gap + (cols > 1 ? Math.Max(0, w - used) / (cols - 1) : 0);
            _cols = Math.Max(1, cols);
        }

        private int _cols;

        protected override Size MeasureOverride(Size availableSize)
        {
            using var diagnosticMeasurement = GetDiagnosticProbe()?.Begin(LayoutPass.Measure, Children.Count) ?? default;
            long childMeasureCalls = 0;
            long childMeasureWallTicks = 0;
            Solve(availableSize.Width);
            if (Math.Abs(ItemWidth - _slot) > 0.5)
                SetValue(ItemWidthPropertyKey, _slot);
            _rowHeight = 0;
            var childConstraint = new Size(_slot, double.PositiveInfinity);
            foreach (UIElement child in Children)
            {
                if (diagnosticMeasurement.IsEnabled)
                {
                    long childStarted = Stopwatch.GetTimestamp();
                    child.Measure(childConstraint);
                    childMeasureWallTicks += Stopwatch.GetTimestamp() - childStarted;
                    childMeasureCalls++;
                }
                else
                {
                    child.Measure(childConstraint);
                }

                var h = child.DesiredSize.Height;
                if (double.IsInfinity(h) || double.IsNaN(h)) h = 0;
                if (h > _rowHeight) _rowHeight = h;
            }
            diagnosticMeasurement.RecordChildCalls(childMeasureCalls, childMeasureWallTicks, 0, 0);
            return new Size(availableSize.Width, ComputeExtent());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            using var diagnosticMeasurement = GetDiagnosticProbe()?.Begin(LayoutPass.Arrange, Children.Count) ?? default;
            long childMeasureCalls = 0;
            long childMeasureWallTicks = 0;
            long childArrangeCalls = 0;
            long childArrangeWallTicks = 0;
            Solve(finalSize.Width);
            if (Math.Abs(ItemWidth - _slot) > 0.5)
                SetValue(ItemWidthPropertyKey, _slot);

            // Recompute _rowHeight if not yet measured (Arrange can be called before Measure)
            if (_rowHeight <= 0 && Children.Count > 0)
            {
                var childConstraint = new Size(_slot, double.PositiveInfinity);
                foreach (UIElement child in Children)
                {
                    if (diagnosticMeasurement.IsEnabled)
                    {
                        long childStarted = Stopwatch.GetTimestamp();
                        child.Measure(childConstraint);
                        childMeasureWallTicks += Stopwatch.GetTimestamp() - childStarted;
                        childMeasureCalls++;
                    }
                    else
                    {
                        child.Measure(childConstraint);
                    }

                    var h = child.DesiredSize.Height;
                    if (double.IsInfinity(h) || double.IsNaN(h)) h = 0;
                    if (h > _rowHeight) _rowHeight = h;
                }
            }

            var rowHeight = Math.Max(0, _rowHeight);
            var y = 0d;
            var count = Children.Count;
            for (var i = 0; i < count; i++)
            {
                var child = Children[i];
                var col = i % _cols;
                var x = col * (_slot + _gapH);
                var childRect = new Rect(x, y, _slot, rowHeight > 0 ? rowHeight : Math.Max(0, child.DesiredSize.Height));
                if (diagnosticMeasurement.IsEnabled)
                {
                    long childStarted = Stopwatch.GetTimestamp();
                    child.Arrange(childRect);
                    childArrangeWallTicks += Stopwatch.GetTimestamp() - childStarted;
                    childArrangeCalls++;
                }
                else
                {
                    child.Arrange(childRect);
                }

                if (col == _cols - 1) y += rowHeight + Gap;
            }
            diagnosticMeasurement.RecordChildCalls(childMeasureCalls, childMeasureWallTicks, childArrangeCalls, childArrangeWallTicks);
            return new Size(finalSize.Width, ComputeExtent());
        }

        private double ComputeExtent()
        {
            var rows = _cols > 0 ? (Children.Count + _cols - 1) / _cols : 0;
            return rows > 0 ? (rows - 1) * (_rowHeight + Math.Max(0, Gap)) + _rowHeight : 0;
        }
    }
}
