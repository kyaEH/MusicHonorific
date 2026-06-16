using Dalamud.Configuration;
using System;

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

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
