using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Dalamud.Utility;
using NPSMLib;

namespace MusicHonorific;

/// <summary>
/// Watches the Windows "Now Playing" media session via NPSMLib, an in-process COM wrapper
/// around the NowPlayingSessionManager API (the same data the WinRT
/// GlobalSystemMediaTransportControls projection exposes). NPSMLib talks to the COM server
/// directly, so it avoids the CsWinRT global ComWrappers conflict ("Attempt to update
/// previously set global instance") that prevented calling the WinRT projection in-process
/// and previously forced an out-of-process PowerShell bridge.
/// </summary>
public sealed class MediaWatcher : IDisposable
{
    private readonly System.Timers.Timer timer;
    private readonly object queryLock = new();

    // Created lazily on a thread-pool (MTA) thread so the COM object and every call that
    // touches it share a single apartment.
    private NowPlayingSessionManager? sessionManager;
    private bool initFailed;

    /// <summary>
    /// Source toggles read just before each query. When a category is disabled, its sessions
    /// are skipped entirely so a disabled source cannot block an enabled one that is also playing.
    /// </summary>
    public bool AllowDeezer { get; set; } = true;
    public bool AllowSpotify { get; set; } = true;
    public bool AllowOther { get; set; } = true;

    /// <summary>Parsed song title. Empty if nothing is playing.</summary>
    public string Song { get; private set; } = string.Empty;

    /// <summary>Parsed artist name. Empty if nothing is playing.</summary>
    public string Artist { get; private set; } = string.Empty;

    /// <summary>True when a matching media session is found.</summary>
    public bool IsRunning { get; private set; }
    public bool IsPlaying { get; private set; }
    public string SourceAppId { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Live diagnostics about the last query.</summary>
    public string Diagnostics { get; private set; } = "Not refreshed yet.";

    public MediaWatcher()
    {
        timer = new System.Timers.Timer(5000);
        timer.Elapsed += OnTick;
        timer.AutoReset = true;
        timer.Start();
        Refresh();
    }

    /// <summary>Force an immediate refresh outside of the timer interval.</summary>
    /// <remarks>
    /// Dispatched onto the thread pool so the COM object is always created and used from an
    /// MTA thread, even when this is called from the game's main thread (e.g. the UI button).
    /// </remarks>
    public void Refresh() => Task.Run(RunQuery);

    // Timer callbacks already run on an MTA thread-pool thread.
    private void OnTick(object? sender, ElapsedEventArgs e) => RunQuery();

    private void RunQuery()
    {
        // Skip if a query is already in flight rather than queueing another up.
        if (!Monitor.TryEnter(queryLock)) return;
        try
        {
            Query();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[MediaWatcher] {ex}");
            LastError = ex.Message;
            Diagnostics = $"Exception: {ex.GetType().Name}: {ex.Message}";
            ResetState();
        }
        finally
        {
            Monitor.Exit(queryLock);
        }
    }

    private void Query()
    {
        if (initFailed) return;

        if (Util.IsWine())
        {
            ResetState();
            LastError = "The Windows media API is unavailable under Wine/Linux.";
            Diagnostics = LastError;
            return;
        }

        NowPlayingSessionManager mgr;
        try
        {
            mgr = sessionManager ??= new NowPlayingSessionManager();
        }
        catch (Exception ex)
        {
            initFailed = true; // unsupported OS / COM unavailable — stop retrying every tick
            ResetState();
            LastError = ex.Message;
            Diagnostics = $"Failed to create NowPlayingSessionManager: {ex.Message}";
            return;
        }

        var sessions = mgr.GetSessions();
        if (sessions == null || sessions.Length == 0)
        {
            ResetState();
            LastError = string.Empty;
            Diagnostics = "No active media session found.";
            return;
        }

        // Evaluate each allowed session, capturing its data source and playback state.
        var candidates = new List<Candidate>();
        foreach (var session in sessions)
        {
            string sourceId;
            try { sourceId = session.SourceAppId ?? string.Empty; }
            catch { continue; }

            if (!IsSourceAllowed(sourceId)) continue;

            try
            {
                var src = session.ActivateMediaPlaybackDataSource();
                var playing = src.GetMediaPlaybackInfo().PlaybackState == MediaPlaybackState.Playing;
                candidates.Add(new Candidate(src, sourceId, playing, GetSourcePriority(sourceId)));
            }
            catch
            {
                // Session went away or refused activation between enumeration and read — skip it.
            }
        }

        if (candidates.Count == 0)
        {
            ResetState();
            LastError = string.Empty;
            Diagnostics = "No active media session found.";
            return;
        }

        // Prefer a session that is currently playing; tie-break by known source priority
        // (Deezer desktop > Deezer RPC > Spotify > browsers/YouTube/other).
        var playingOnly = candidates.Where(c => c.IsPlaying).ToList();
        var pool = playingOnly.Count > 0 ? playingOnly : candidates;
        var best = pool.OrderBy(c => c.Priority).First();

        MediaObjectInfo info;
        try
        {
            info = best.Source.GetMediaObjectInfo();
        }
        catch (Exception ex)
        {
            ResetState();
            LastError = ex.Message;
            Diagnostics = $"Failed to read media info: {ex.Message}";
            return;
        }

        Song = (info.Title ?? string.Empty).Trim();
        Artist = (info.Artist ?? string.Empty).Trim();
        SourceAppId = best.SourceId;
        IsPlaying = best.IsPlaying;
        IsRunning = true;
        LastError = string.Empty;
        Diagnostics = $"OK source=[{SourceAppId}] status={(IsPlaying ? "Playing" : "Paused")}";
    }

    /// <summary>True when the given source's category is enabled in the config.</summary>
    private bool IsSourceAllowed(string sourceId)
    {
        var low = sourceId.ToLowerInvariant();
        if (low.Contains("deezer")) return AllowDeezer;
        if (low.Contains("spotify")) return AllowSpotify;
        return AllowOther;
    }

    /// <summary>
    /// Lower numbers win: Deezer desktop, then Deezer RPC, then Spotify, then everything else.
    /// </summary>
    private static int GetSourcePriority(string sourceId)
    {
        var low = sourceId.ToLowerInvariant();
        if (low.Contains("deezer-desktop")) return 0;
        if (low.Contains("deezer")) return 1;
        if (low.Contains("spotify")) return 2;
        return 3;
    }

    private void ResetState()
    {
        IsRunning = false;
        IsPlaying = false;
        Song = string.Empty;
        Artist = string.Empty;
        SourceAppId = string.Empty;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }

    /// <summary>A single allowed media session captured during a query, with its playback state.</summary>
    private readonly record struct Candidate(
        MediaPlaybackDataSource Source, string SourceId, bool IsPlaying, int Priority);
}
