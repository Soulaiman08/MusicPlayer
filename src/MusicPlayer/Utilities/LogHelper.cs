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
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex.Message}\n{ex.StackTrace}\n\n";
            _logQueue.Enqueue((logFile, null, entry));
            _wake.Set();
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
        _running = false;
        _wake.Set();
        DrainQueue();
    }
}
