using Dalamud.Configuration;
using System;
using System.Numerics;

namespace MusicHonorific;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool EnableHonorificSync { get; set; } = true;

    // Per-source toggles. When a source is disabled it is ignored even if it is the one playing.
    public bool AllowDeezer { get; set; } = true;
    public bool AllowSpotify { get; set; } = true;
    public bool AllowOther { get; set; } = true;

    // Honorific title colors (RGB, 0-1 range).
    public Vector3 TextColor { get; set; } = new(1f, 1f, 1f);          // white
    public Vector3 GlowColor { get; set; } = new(0.635f, 0.220f, 1f);  // Deezer purple #A238FF

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
