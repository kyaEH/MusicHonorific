using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MusicHonorific;

/// <summary>
/// Wraps the Honorific plugin IPC to set/clear the local player's custom title.
/// Honorific must be installed and enabled for calls to have any effect.
/// </summary>
public sealed class HonorificIpc
{
    private const int MaxTitleLength = 32;

    private readonly ICallGateSubscriber<int, string, object> setTitle;
    private readonly ICallGateSubscriber<int, object> clearTitle;

    public HonorificIpc(IDalamudPluginInterface pluginInterface)
    {
        setTitle = pluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        clearTitle = pluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
    }

    /// <summary>Sets the local player's Honorific title to the given text.</summary>
    /// <param name="title">Title text.</param>
    /// <param name="isPrefix">Whether the title appears before the name.</param>
    /// <param name="color">Optional RGB text color in 0-1 range.</param>
    /// <param name="glow">Optional RGB glow color in 0-1 range.</param>
    public void SetTitle(string title, bool isPrefix = false, (float R, float G, float B)? color = null, (float R, float G, float B)? glow = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        if (title.Length > MaxTitleLength)
            title = title[..MaxTitleLength].TrimEnd();

        try
        {
            var payloadObj = new Dictionary<string, object>
            {
                ["Title"] = title,
                ["IsPrefix"] = isPrefix,
            };
            if (color.HasValue)
            {
                var c = color.Value;
                payloadObj["Color"] = new { X = c.R, Y = c.G, Z = c.B };
            }
            if (glow.HasValue)
            {
                var g = glow.Value;
                payloadObj["Glow"] = new { X = g.R, Y = g.G, Z = g.B };
            }
            var payload = JsonSerializer.Serialize(payloadObj);
            setTitle.InvokeAction(0, payload);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[HonorificIpc] SetTitle failed: {ex.Message}");
        }
    }

    /// <summary>Clears the local player's Honorific title.</summary>
    public void ClearTitle()
    {
        try
        {
            clearTitle.InvokeAction(0);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[HonorificIpc] ClearTitle failed: {ex.Message}");
        }
    }
}
