using YolanLive.Core.Models;
using YolanLive.WinUI.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YolanLive.WinUI.Helper;

internal sealed class PinnedGame
{
    public string Name { get; set; }
    public List<LiveSubCategory> Sources { get; set; } = new();
}

internal static class BrowsePreferences
{
    internal const string PlatformKey = "BrowsePlatform";
    internal static string GroupKey(Site site) => "BrowseGroup:" + site.SiteType;
    internal static string PinsKey(Site site) => "PinnedGames:" + site.SiteType;

    private static T Read<T>(string key, T fallback)
    {
        try { return SettingHelper.GetValue(key, fallback); }
        catch (JsonException) { return fallback; }
        catch (ArgumentException) { return fallback; }
    }

    internal static Site ReadPlatform() => MainVM.Sites.FirstOrDefault(s => s.SiteType.ToString() == Read(PlatformKey, "")) ?? MainVM.Sites[0];
    internal static LiveCategory RestoreGroup(Site site, IEnumerable<LiveCategory> groups) =>
        groups.FirstOrDefault(g => g.ID == Read(GroupKey(site), "")) ?? groups.FirstOrDefault();

    private static string SourceKey(LiveSubCategory source) => source.ParentID + ":" + source.ID;
    private static string GameKey(IEnumerable<LiveSubCategory> sources) => string.Join("|", sources.Select(SourceKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));
    internal static List<PinnedGame> ReadPins(Site site) => Read(PinsKey(site), new List<PinnedGame>())
        .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name) && p.Sources?.Count > 0 &&
            p.Sources.All(s => s != null && !string.IsNullOrWhiteSpace(s.ID) && !string.IsNullOrWhiteSpace(s.ParentID)))
        .DistinctBy(p => GameKey(p.Sources)).Take(6).ToList();

    internal static bool IsPinned(Site site, LiveGameCategory game) => ReadPins(site).Any(p => GameKey(p.Sources) == GameKey(game.Sources.Select(s => s.Category)));
    internal static bool TogglePin(Site site, LiveGameCategory game)
    {
        if (game.Sources == null || game.Sources.Count == 0 || game.Sources.Any(s => s.Site != site.LiveSite)) throw new ArgumentException("Invalid game platform.");
        var pins = ReadPins(site);
        var key = GameKey(game.Sources.Select(s => s.Category));
        var existing = pins.FindIndex(p => GameKey(p.Sources) == key);
        if (existing >= 0) pins.RemoveAt(existing);
        else
        {
            if (pins.Count == 6) return false;
            pins.Add(new PinnedGame { Name = game.Name, Sources = game.Sources.Select(s => new LiveSubCategory { ID = s.Category.ID, ParentID = s.Category.ParentID }).ToList() });
        }
        SettingHelper.SetValue(PinsKey(site), pins);
        return true;
    }
    internal static void RemovePin(Site site, PinnedGame pin) => SettingHelper.SetValue(PinsKey(site), ReadPins(site).Where(p => GameKey(p.Sources) != GameKey(pin.Sources)).ToList());

    internal static LiveGameCategory ResolvePin(Site site, PinnedGame pin, IEnumerable<LiveGameSource> catalog)
    {
        var sources = catalog.Where(s => s.Site == site.LiveSite && pin.Sources.Any(p => SourceKey(p) == SourceKey(s.Category))).ToList();
        if (GameKey(sources.Select(s => s.Category)) != GameKey(pin.Sources)) return null;
        return new LiveGameCategory { Name = sources[0].Category.Name, Pic = sources[0].Category.Pic, Sources = sources };
    }
}
