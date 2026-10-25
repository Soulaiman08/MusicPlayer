using System;
using System.Threading;
using System.Windows.Media.Animation;

namespace MusicPlayer.Views;

// Single source of truth for navigation motion. Every view/tab/hero transition
// takes its durations and easings from MotionTokens so the whole app moves with
// one consistent language (the scattered 120→1000ms literals were the reason
// each surface felt like a different app). Values below match the long-standing
// behavior 1:1 — unifying them changes nothing visually, it only centralizes.
public static class MotionTokens
{
    public static TimeSpan PageEnter => TimeSpan.FromMilliseconds(280);
    public static TimeSpan HeroMorph => TimeSpan.FromMilliseconds(260);
    public static TimeSpan TabSlide => TimeSpan.FromMilliseconds(260);
    public static TimeSpan ReverseRevealLead => TimeSpan.FromMilliseconds(40);
    public static TimeSpan ReverseRevealFollow => TimeSpan.FromMilliseconds(110);
    public static TimeSpan ReverseRevealLeadDuration => TimeSpan.FromMilliseconds(150);
    public static TimeSpan ReverseRevealFollowDuration => TimeSpan.FromMilliseconds(160);
    public static TimeSpan OverlayMeltBegin => TimeSpan.FromMilliseconds(190);
    public static TimeSpan OverlayMeltDuration => TimeSpan.FromMilliseconds(70);
    public static TimeSpan CardRevealDelay => TimeSpan.FromMilliseconds(190);
    public static TimeSpan RevealGatePoll => TimeSpan.FromMilliseconds(50);
    public static TimeSpan RevealGateCap => TimeSpan.FromMilliseconds(500);
    public static TimeSpan BackUnblockTimeout => TimeSpan.FromMilliseconds(1000);

    public static CubicEase EaseOut { get; } = CreateEase(EasingMode.EaseOut);
    public static CubicEase EaseIn { get; } = CreateEase(EasingMode.EaseIn);

    private static CubicEase CreateEase(EasingMode mode)
    {
        var ease = new CubicEase { EasingMode = mode };
        if (ease.CanFreeze) ease.Freeze();
        return ease;
    }
}

// Generation counter for navigation side-effects. Fire-and-forget timers that
// touch shared transition state (reveal gate, card reveal, back unblock)
// capture the generation at arm time and no-op when stale, so a superseding
// navigation can never be corrupted by a previous run's delayed callback.
public static class NavGate
{
    private static long _generation;

    public static long NewNavigation() => Interlocked.Increment(ref _generation);

    public static bool IsCurrent(long generation) =>
        Interlocked.Read(ref _generation) == generation;
}
