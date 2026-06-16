using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MusicHonorific.Windows;

namespace MusicHonorific;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/musichonorific";

    public Configuration Configuration { get; init; }
    public MediaWatcher MediaWatcher { get; init; }
    public HonorificIpc HonorificIpc { get; init; }
    private string lastHonorificTitle = string.Empty;
    private DateTime? syncCycleStart; // set when song begins, never reset on song change

    public readonly WindowSystem WindowSystem = new("MusicHonorific");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MediaWatcher = new MediaWatcher();
        HonorificIpc = new HonorificIpc(PluginInterface);
        Framework.Update += OnFrameworkUpdate;

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Music Honorific window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"MusicHonorific loaded.");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Framework.Update -= OnFrameworkUpdate;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        MediaWatcher.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Keep the watcher's source filter in sync with the config.
        MediaWatcher.AllowDeezer = Configuration.AllowDeezer;
        MediaWatcher.AllowSpotify = Configuration.AllowSpotify;
        MediaWatcher.AllowOther = Configuration.AllowOther;

        if (!Configuration.EnableHonorificSync) return;

        var currentSong = MediaWatcher.Song;
        var currentArtist = MediaWatcher.Artist;

        if (string.IsNullOrEmpty(currentSong) || !MediaWatcher.IsPlaying)
        {
            // Nothing playing — clear and reset cycle
            if (syncCycleStart.HasValue)
            {
                syncCycleStart = null;
                lastHonorificTitle = string.Empty;
                HonorificIpc.ClearTitle();
            }
            return;
        }

        // Song is playing — start cycle timer on first song
        syncCycleStart ??= DateTime.UtcNow;

        // 60-second cycle: 0–49s = normal title, 50–59s = branding.
        // The branding alternates each cycle between the source message and the plugin promo.
        var elapsed = (DateTime.UtcNow - syncCycleStart.Value).TotalSeconds;
        var secsInCycle = elapsed % 60.0;
        var cycleIndex = (long)(elapsed / 60.0);
        string titleText;
        if (secsInCycle >= 50.0)
        {
            titleText = cycleIndex % 2 == 0
                ? BuildBrandingTitle(MediaWatcher.SourceAppId)
                : "\u266a Synced via MusicHonorific \u266a";
        }
        else
        {
            titleText = BuildHonorificTitle(currentSong, currentArtist, elapsed);
        }

        if (titleText == lastHonorificTitle) return;

        lastHonorificTitle = titleText;
        HonorificIpc.SetTitle(titleText, glow: (0.635f, 0.220f, 1.0f)); // Accent purple #A238FF
    }

    /// <summary>
    /// Builds the periodic branding title based on the playback source:
    /// "♪ Listening on Deezer ♪", "♪ Listening on Spotify ♪", or "♪ Listening to music ♪".
    /// </summary>
    private static string BuildBrandingTitle(string sourceAppId)
    {
        var id = sourceAppId ?? string.Empty;
        if (id.Contains("deezer", StringComparison.OrdinalIgnoreCase))
            return "\u266a Listening on Deezer \u266a";
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            return "\u266a Listening on Spotify \u266a";
        return "\u266a Listening to music \u266a";
    }

    // Marquee tuning.
    private const string TitlePrefix = "♪ ";
    private const string TitleSuffix = " ♪";
    private const string TitleSeparator = " - ";
    private const int TitleMaxLen = 32;
    private const double ScrollStepSeconds = 0.3;  // time per one-character advance
    private const double ScrollPauseSeconds = 5; // hold at the start and the end before looping

    /// <summary>
    /// Builds the Honorific title string. The ♪ prefix and suffix stay fixed while the
    /// "Artist - Song" text scrolls (marquee) one character at a time when it is too long
    /// to fit in 32 characters, pausing at the start and end before looping.
    /// </summary>
    private static string BuildHonorificTitle(string song, string artist, double timeSeconds)
    {
        if (string.IsNullOrEmpty(song)) return string.Empty;

        var content = string.IsNullOrEmpty(artist) ? song : artist + TitleSeparator + song;

        // Whole thing fits — show it static.
        var full = TitlePrefix + content + TitleSuffix;
        if (full.Length <= TitleMaxLen) return full;

        // Otherwise scroll the content through a fixed-width window between the notes.
        var window = TitleMaxLen - TitlePrefix.Length - TitleSuffix.Length;
        var maxOffset = content.Length - window; // last valid start index

        var scrollDuration = maxOffset * ScrollStepSeconds;
        var totalDuration = ScrollPauseSeconds + scrollDuration + ScrollPauseSeconds;
        var t = timeSeconds % totalDuration;

        int offset;
        if (t < ScrollPauseSeconds)
            offset = 0; // hold at the beginning
        else if (t < ScrollPauseSeconds + scrollDuration)
            offset = (int)((t - ScrollPauseSeconds) / ScrollStepSeconds); // sliding
        else
            offset = maxOffset; // hold at the end

        if (offset < 0) offset = 0;
        if (offset > maxOffset) offset = maxOffset;

        return TitlePrefix + content.Substring(offset, window) + TitleSuffix;
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    /// <summary>
    /// Resets the Honorific sync cycle so the song title is pushed immediately on the next frame.
    /// Call this when the user manually refreshes while sync is enabled.
    /// </summary>
    public void ForceHonorificUpdate()
    {
        syncCycleStart = DateTime.UtcNow;
        lastHonorificTitle = string.Empty; // invalidate cache so OnFrameworkUpdate pushes immediately
    }
}