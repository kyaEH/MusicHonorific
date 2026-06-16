using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MusicHonorific.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Music Honorific Configuration###MusicHonorificConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(280, 230);
        SizeCondition = ImGuiCond.Always;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var syncEnabled = configuration.EnableHonorificSync;
        if (ImGui.Checkbox("Sync song to Honorific title", ref syncEnabled))
        {
            configuration.EnableHonorificSync = syncEnabled;
            configuration.Save();
            if (!syncEnabled)
                plugin.HonorificIpc.ClearTitle();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Requires the Honorific plugin to be installed and enabled.");

        ImGui.Separator();
        ImGui.TextUnformatted("Listen to these sources:");

        var allowDeezer = configuration.AllowDeezer;
        if (ImGui.Checkbox("Deezer", ref allowDeezer))
        {
            configuration.AllowDeezer = allowDeezer;
            configuration.Save();
        }

        var allowSpotify = configuration.AllowSpotify;
        if (ImGui.Checkbox("Spotify", ref allowSpotify))
        {
            configuration.AllowSpotify = allowSpotify;
            configuration.Save();
        }

        var allowOther = configuration.AllowOther;
        if (ImGui.Checkbox("Other sources (YouTube, browser...)", ref allowOther))
        {
            configuration.AllowOther = allowOther;
            configuration.Save();
        }
    }
}
