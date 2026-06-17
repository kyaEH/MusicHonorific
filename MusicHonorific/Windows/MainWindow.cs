using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace MusicHonorific.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Music Honorific##MusicHonorificMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Show Settings"))
            plugin.ToggleConfigUi();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Now playing ───────────────────────────────────────────────
        var watcher = plugin.MediaWatcher;
        ImGui.TextColored(new Vector4(0.45f, 0.76f, 1f, 1f), "Now Playing (Windows Media Session)");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
        {
            watcher.Refresh();
            if (plugin.Configuration.EnableHonorificSync)
                plugin.ForceHonorificUpdate();
        }

        ImGui.Spacing();

        if (!watcher.IsRunning)
        {
            ImGui.TextDisabled("No active media session found.");
            ImGui.TextDisabled("Start playback (Deezer, Spotify, browser...) and press Refresh.");
            if (!string.IsNullOrWhiteSpace(watcher.LastError))
                ImGui.TextDisabled($"Last error: {watcher.LastError}");
        }
        else if (string.IsNullOrWhiteSpace(watcher.Song))
        {
            ImGui.TextDisabled("Session found, but no track metadata.");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.92f, 0.35f, 1f), watcher.Song);
            if (!string.IsNullOrEmpty(watcher.Artist))
                ImGui.TextDisabled(watcher.Artist);

            ImGui.TextDisabled(watcher.IsPlaying ? "Status: Playing" : "Status: Paused");
            if (!string.IsNullOrWhiteSpace(watcher.SourceAppId))
                ImGui.TextDisabled($"Source: {watcher.SourceAppId}");
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Diagnostics"))
        {
            ImGui.TextWrapped(watcher.Diagnostics);
            if (!string.IsNullOrWhiteSpace(watcher.LastError))
                ImGui.TextWrapped($"Last error: {watcher.LastError}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── About ─────────────────────────────────────────────────────
        using var about = ImRaii.Child("AboutSection", new Vector2(0, 0), false);
        if (!about.Success) return;

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Author");
        ImGui.SameLine();
        ImGui.Text("Kyaeh / Pastalix / Lianh Procyon");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Inspired by");
        ImGui.Indent();
        ImGui.TextDisabled("SpotifyHonorific  (Valiice)");
        ImGui.TextDisabled("PatMeHonorific  (anya-hichu)");
        ImGui.TextDisabled("NowPlaying  (wompscode)");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Built from");
        ImGui.Indent();
        ImGui.TextDisabled("SamplePlugin  (goatcorp)");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Data source");
        ImGui.SameLine();
        ImGui.TextDisabled("Windows GlobalSystemMediaTransportControlsSessionManager");
    }
}
