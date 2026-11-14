using System;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Target image quality tier for the unified ArtworkPipeline.
    /// </summary>
    public enum ArtworkQuality
    {
        /// <summary>80px square: Track list rows and track grid cards.</summary>
        List = 80,

        /// <summary>160px square: MiniPlayer album artwork.</summary>
        Mini = 160,

        /// <summary>240px square: Album and Artist cards in grid views.</summary>
        Cards = 240,

        /// <summary>Full original resolution: Album details and FullPlayer view.</summary>
        Full = 0
    }

    /// <summary>
    /// Strict priority levels for image decoding and pipeline scheduling.
    /// Lower numeric value = higher processing priority.
    /// </summary>
    public enum ArtworkPriority
    {
        /// <summary>Elements currently visible directly in front of user.</summary>
        ImmediateVisible = 0,

        /// <summary>Initial viewport elements preloading before loading screen is dismissed.</summary>
        StartupViewport = 1,

        /// <summary>Off-screen elements belonging to the currently active tab.</summary>
        CurrentTab = 2,

        /// <summary>Background library elements not currently visible on screen.</summary>
        BackgroundLibrary = 3,

        /// <summary>Idle-only maintenance tasks (disk cache derivative pre-generation, cleanup).</summary>
        Maintenance = 4
    }
}

