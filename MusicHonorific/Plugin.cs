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

        // 60-second cycle: 0–49s = normal title, 50–59s = source branding
        var secsInCycle = (DateTime.UtcNow - syncCycleStart.Value).TotalSeconds % 60.0;
        var titleText = secsInCycle >= 50.0
            ? BuildBrandingTitle(MediaWatcher.SourceAppId)
            : BuildHonorificTitle(currentSong, currentArtist);

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

    /// <summary>
    /// Builds the Honorific title string:
    /// ♪ Song  (if song alone fits in 32 chars)
    /// ♪ Song - Ar...  (artist truncated with "..." if space remains)
    /// </summary>
    private static string BuildHonorificTitle(string song, string artist)
    {
        if (string.IsNullOrEmpty(song)) return string.Empty;

        const string prefix = "♪ ";
        const string suffix = " ♪";
        const string separator = " - ";
        const int maxLen = 32;

        // Full ideal: ♪ Artist - Song ♪
        var full = prefix + (string.IsNullOrEmpty(artist) ? song : artist + separator + song) + suffix;
        if (full.Length <= maxLen) return full;

        if (string.IsNullOrEmpty(artist))
        {
            // No artist, truncate song and append suffix
            var available = maxLen - prefix.Length - suffix.Length;
            return prefix + (song.Length <= available ? song : song[..available].TrimEnd()) + suffix;
        }

        // Try: ♪ Artist... - Song ♪ (truncate artist to make room for full song + suffix)
        var songSection = separator + song + suffix;
        var artistAvailable = maxLen - prefix.Length - songSection.Length;
        if (artistAvailable >= 4) // room for at least "A..."
        {
            var truncatedArtist = artist.Length <= artistAvailable
                ? artist
                : artist[..(artistAvailable - 3)].TrimEnd() + "...";
            return prefix + truncatedArtist + songSection;
        }

        // Song + suffix alone — truncate song if needed
        var songOnly = prefix + song + suffix;
        if (songOnly.Length <= maxLen) return songOnly;
        var songAvail = maxLen - prefix.Length - suffix.Length;
        return prefix + song[..songAvail].TrimEnd() + suffix;
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