using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace MusicPlayer.Utilities;

public static class LogHelper
{
    public static ILoggerFactory? LoggerFactory { get; set; }
    private static readonly ConcurrentQueue<(string logFile, string? mirrorFile, string line)> _logQueue = new();
    private static readonly AutoResetEvent _wake = new(false);
    private static readonly Thread _writerThread;
    private static volatile bool _running = true;
    private static readonly object _flushLock = new();

    static LogHelper()
    {
        _writerThread = new Thread(ProcessLogQueue)
        {
            IsBackground = true,
            Name = "LogWriterThread",
            Priority = ThreadPriority.Lowest
        };
        _writerThread.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    public static void LogError(string context, Exception ex)
    {
        var logger = LoggerFactory?.CreateLogger("MusicPlayer");
        logger?.LogError(ex, "[{Context}] {Message}", context, ex.Message);

        try
        {
            var logFile = Path.Combine(AppPaths.LogsPath, $"error_{DateTime.Now:yyyyMMdd}.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex}\n\n";
            File.AppendAllText(logFile, entry);
        }
        catch { }
    }

    public static void LogInfo(string message)
    {
        var logger = LoggerFactory?.CreateLogger("MusicPlayer");
        logger?.LogInformation("{Message}", message);

        try
        {
            var logFile = Path.Combine(AppPaths.LogsPath, $"info_{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";

            string? docLog = null;
            try
            {
                var docsDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\docs\runtime_logs"));
                if (Directory.Exists(docsDir))
                {
                    docLog = Path.Combine(docsDir, "playback_diagnostic.log");
                }
            }
            catch { }

            if (!_running)
            {
                // Shutdown in progress: write synchronously (best effort) instead of
                // enqueueing into a queue that may never be drained.
                try { File.AppendAllText(logFile, line); } catch { }
                if (docLog != null) try { File.AppendAllText(docLog, line); } catch { }
                return;
            }
            _logQueue.Enqueue((logFile, docLog, line));
            _wake.Set();
        }
        catch { }
    }

    private static void ProcessLogQueue()
    {
        while (_running)
        {
            _wake.WaitOne(1000);
            DrainQueue();
        }
        DrainQueue();
    }

    private static void DrainQueue()
    {
        try
        {
            while (_logQueue.TryDequeue(out var item))
            {
                try { File.AppendAllText(item.logFile, item.line); } catch { }
                if (item.mirrorFile != null)
                {
                    try { File.AppendAllText(item.mirrorFile, item.line); } catch { }
                }
            }
        }
        catch { }
    }

    public static void Flush()
    {
        lock (_flushLock)
        {
            if (_running)
            {
                _running = false;
                _wake.Set();
                try { _writerThread.Join(2000); } catch { }
            }

            // Only drain synchronously if the writer thread is no longer running,
            // otherwise we would race with it on the log files.
            if (!_writerThread.IsAlive)
            {
                DrainQueue();
            }
            else
            {
                try { File.AppendAllText(Path.Combine(AppPaths.LogsPath, "error_" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [LogHelper] Flush timed out; writer thread still alive\n"); } catch { }
            }
        }
    }
}
