namespace MusicPlayer.ViewModels
{
    public enum ShutdownOutcome
    {
        Completed,
        Pending,
        Quarantined,
        Failed
    }

    public sealed record ApplicationShutdownResult(
        ShutdownOutcome Outcome,
        bool CanDisposeServiceProvider,
        IReadOnlyList<string> PendingOperations,
        IReadOnlyList<string> RetainedResources,
        string? FailureReason);
}

namespace MusicPlayer
{
    public enum ShutdownOutcome
    {
        Completed,
        Pending,
        Quarantined,
        Failed
    }

    public sealed record ApplicationShutdownResult(
        ShutdownOutcome Outcome,
        bool CanDisposeServiceProvider,
        IReadOnlyList<string> PendingOperations,
        IReadOnlyList<string> RetainedResources,
        string? FailureReason);
}

public enum ShutdownOutcome
{
    Completed,
    Pending,
    Quarantined,
    Failed
}

public sealed record ApplicationShutdownResult(
    ShutdownOutcome Outcome,
    bool CanDisposeServiceProvider,
    IReadOnlyList<string> PendingOperations,
    IReadOnlyList<string> RetainedResources,
    string? FailureReason);

namespace MusicPlayer.Utilities
{
    public enum ShutdownOutcome
    {
        Completed,
        Pending,
        Quarantined,
        Failed
    }

    public sealed record ApplicationShutdownResult(
        ShutdownOutcome Outcome,
        bool CanDisposeServiceProvider,
        IReadOnlyList<string> PendingOperations,
        IReadOnlyList<string> RetainedResources,
        string? FailureReason);
}