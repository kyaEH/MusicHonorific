using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MusicHonorific;

/// <summary>
/// Watches the Windows "Now Playing" media session (GlobalSystemMediaTransportControls).
/// The query runs out-of-process via PowerShell to avoid the CsWinRT global ComWrappers
/// registration conflict ("Attempt to update previously set global instance") that occurs
/// when calling WinRT projections in-process inside the Dalamud host.
/// </summary>
public sealed class MediaWatcher : IDisposable
{
    private readonly System.Timers.Timer timer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    /// <summary>Raw title representation for display/debug.</summary>
    public string RawTitle { get; private set; } = string.Empty;

    /// <summary>Parsed song title. Empty if nothing is playing.</summary>
    public string Song { get; private set; } = string.Empty;

    /// <summary>Parsed artist name. Empty if nothing is playing.</summary>
    public string Artist { get; private set; } = string.Empty;

    /// <summary>True when a matching media session is found.</summary>
    public bool IsRunning { get; private set; } = false;
    public bool IsPlaying { get; private set; } = false;
    public string SourceAppId { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Live diagnostics about the last query.</summary>
    public string Diagnostics { get; private set; } = "Not refreshed yet.";

    public MediaWatcher()
    {
        timer = new System.Timers.Timer(3000);
        timer.Elapsed += OnTick;
        timer.AutoReset = true;
        timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Force an immediate refresh outside of the timer interval.</summary>
    public void Refresh() => _ = RefreshAsync();

    private void OnTick(object? sender, ElapsedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (!await refreshLock.WaitAsync(0)) return;

        try
        {
            var output = await RunQueryAsync();
            ParseOutput(output);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[MediaSessionWatcher] {ex}");
            LastError = ex.Message;
            Diagnostics = $"Exception: {ex.GetType().Name}: {ex.Message}";
            ResetState();
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static async Task<string> RunQueryAsync()
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(QueryScript));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!await Task.Run(() => process.WaitForExit(8000)))
        {
            try { process.Kill(); } catch { /* ignore */ }
            return "ERR\tPowerShell query timed out.";
        }

        var stdout = (await stdoutTask).Trim();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            var stderr = (await stderrTask).Trim();
            return string.IsNullOrWhiteSpace(stderr) ? "NONE\t\t\t" : $"ERR\t{stderr}";
        }

        return stdout;
    }

    private void ParseOutput(string output)
    {
        // Use the last non-empty line in case the host emitted extra noise.
        var lines = output.Split('\n');
        var line = string.Empty;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var candidate = lines[i].Trim('\r', ' ');
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                line = candidate;
                break;
            }
        }

        var parts = line.Split('\t');
        var tag = parts.Length > 0 ? parts[0] : string.Empty;

        switch (tag)
        {
            case "OK":
                Song = (parts.Length > 1 ? parts[1] : string.Empty).Trim();
                Artist = (parts.Length > 2 ? parts[2] : string.Empty).Trim();
                var status = (parts.Length > 3 ? parts[3] : string.Empty).Trim();
                SourceAppId = (parts.Length > 4 ? parts[4] : string.Empty).Trim();

                IsPlaying = status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
                IsRunning = true;
                RawTitle = string.IsNullOrWhiteSpace(Artist) ? Song : $"{Artist} - {Song}";
                LastError = string.Empty;
                Diagnostics = $"OK source=[{SourceAppId}] status={status}";
                break;

            case "NONE":
                ResetState();
                Diagnostics = "No active media session found.";
                break;

            case "ERR":
                ResetState();
                LastError = parts.Length > 1 ? parts[1] : "Unknown error.";
                Diagnostics = $"Query error: {LastError}";
                break;

            default:
                ResetState();
                Diagnostics = $"Unexpected query output: {line}";
                break;
        }
    }

    private void ResetState()
    {
        IsRunning = false;
        IsPlaying = false;
        RawTitle = string.Empty;
        Song = string.Empty;
        Artist = string.Empty;
        SourceAppId = string.Empty;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
        refreshLock.Dispose();
    }

    /// <summary>
    /// PowerShell script that queries the Windows media session API and prints a
    /// tab-separated result: "OK\t{title}\t{artist}\t{status}\t{source}".
    /// </summary>
    private const string QueryScript = @"
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null

    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        })[0]

    function Await($op, $resultType) {
        $task = $asTaskGeneric.MakeGenericMethod($resultType).Invoke($null, @($op))
        $task.Wait(-1) | Out-Null
        $task.Result
    }

    [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime] | Out-Null

    $mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
    $sessions = @($mgr.GetSessions())

    function Get-SourcePriority($id) {
        if (-not $id) { return 99 }
        $low = $id.ToLower()
        if ($low.Contains('deezer-desktop')) { return 0 }
        if ($low.Contains('deezer')) { return 1 }
        if ($low.Contains('spotify')) { return 2 }
        return 3
    }

    # Prefer a session that is currently playing; tie-break by known source priority
    # (Deezer desktop > Deezer RPC > Spotify > browsers/YouTube/other).
    $playing = @($sessions | Where-Object { $_.GetPlaybackInfo().PlaybackStatus -eq 'Playing' })
    $pool = if ($playing.Count -gt 0) { $playing } else { $sessions }
    $best = $pool | Sort-Object { Get-SourcePriority $_.SourceAppUserModelId } | Select-Object -First 1
    if (-not $best) { $best = $mgr.GetCurrentSession() }

    if (-not $best) {
        Write-Output ""NONE`t`t`t""
        exit 0
    }

    $media = Await ($best.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
    $status = $best.GetPlaybackInfo().PlaybackStatus
    $title = if ($media) { $media.Title } else { '' }
    $artist = if ($media) { $media.Artist } else { '' }
    $source = $best.SourceAppUserModelId

    Write-Output (""OK`t{0}`t{1}`t{2}`t{3}"" -f $title, $artist, $status, $source)
}
catch {
    Write-Output (""ERR`t{0}"" -f $_.Exception.Message)
}
";
}
