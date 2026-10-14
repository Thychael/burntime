using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Burntime.Data.BurnGfx;
using Burntime.Platform;
using Burntime.Platform.Graphics;
using Burntime.Platform.IO;
using Burntime.Platform.Resource;

namespace Burntime.Remaster.Logic.Generation;

/// <summary>
/// Random extra locations ("sites") on the original world map. The original world stays as it is;
/// a new game adds sites on free sand, each linked to one or two nearby locations and based on an
/// original non-city location (map, rooms, water, production, danger).
/// </summary>
/// <remarks>
/// World map and ways are loaded from resource files and are not part of a save, so the generated
/// sites are stored in <see cref="World.RandomSites"/> and re-applied to the cached map and way data
/// after every new game and load (<see cref="Apply"/>).
/// </remarks>
public static class RandomSites
{
    public const int DefaultCount = 10;

    const int Block = 8;
    const double MinDistance = 36;
    const double PixelsPerDay = 25;
    const int MaxDays = 4;
    const int MaxNeighbors = 4;
    const double MaxLength = (MaxDays + 0.5) * PixelsPerDay;
    const int Attempts = 400;

    /// <summary>Site icon; <paramref name="Entrance"/> is relative to the icon's top left corner.</summary>
    public sealed record SiteIcon(string File, Rect Entrance, Vector2 Size);

    /// <summary>Generation input describing the world map.</summary>
    public sealed record MapInfo(IReadOnlyList<Rect> Entrances, IReadOnlyList<int> Degrees, bool[,] Sand,
        IReadOnlyList<SiteIcon> Icons, IReadOnlyList<LocationTemplate> Templates);

    #region Planning
    public static RandomSiteState Plan(int seed, int count, MapInfo info)
    {
        var random = new WorldRandom(seed);
        var state = new RandomSiteState { Seed = seed, FirstId = info.Entrances.Count, Sites = [], Links = [] };
        var templates = info.Templates.Where(t => !t.IsCity).ToList();
        if (count <= 0 || templates.Count == 0 || info.Icons.Count == 0)
            return state;

        var centers = info.Entrances.Select(e => (Vector2f)e.Center).ToList();
        var degrees = info.Degrees.ToList();
        while (degrees.Count < centers.Count)
            degrees.Add(0);
        var sandBlocks = new List<(int X, int Y)>();
        for (int y = 0; y < info.Sand.GetLength(0); y++)
            for (int x = 0; x < info.Sand.GetLength(1); x++)
                if (info.Sand[y, x])
                    sandBlocks.Add((x, y));
        if (sandBlocks.Count == 0)
            return state;

        var deck = new Queue<LocationTemplate>();
        var names = new WorldGenerator.NameGenerator(random);
        var sites = new List<RandomSite>();
        var links = new List<RandomSiteLink>();

        for (int k = 0; k < count; k++)
        {
            RandomSite? site = null;
            for (int attempt = 0; attempt < Attempts && site == null; attempt++)
            {
                var block = sandBlocks[random.Next(sandBlocks.Count)];
                int icon = random.Next(info.Icons.Count);
                Rect door = info.Icons[icon].Entrance;
                var center = new Vector2f(block.X * Block + random.Next(Block), block.Y * Block + random.Next(Block));
                var entrance = new Rect((int)center.x - door.Width / 2, (int)center.y - door.Height / 2, door.Width, door.Height);
                var iconArea = new Rect(entrance.Position - door.Position, info.Icons[icon].Size);
                if (!OnSand(info.Sand, iconArea) || !OnSand(info.Sand, entrance) ||
                    centers.Any(c => (c - (Vector2f)entrance.Center).Length < MinDistance) ||
                    Candidates(centers, degrees, entrance.Center).Count == 0)
                    continue;

                if (deck.Count == 0)
                    foreach (var t in random.Shuffle(templates.ToList()))
                        deck.Enqueue(t);
                site = new RandomSite
                {
                    Id = state.FirstId + k,
                    X = entrance.Left, Y = entrance.Top, Width = entrance.Width, Height = entrance.Height,
                    Icon = icon, Template = deck.Dequeue().Id, Name = names.Next(),
                };
            }
            if (site == null)
                break; // no free place left; fewer sites is fine

            Vector2 siteCenter = site.Area.Center;
            var candidates = Candidates(centers, degrees, siteCenter);
            int linkCount = candidates.Count > 1 && candidates[1].Distance <= 0.8 * MaxLength &&
                random.NextDouble() < 0.5 ? 2 : 1;
            centers.Add(siteCenter);
            degrees.Add(0);
            foreach (var (target, distance) in candidates.Take(linkCount))
            {
                links.Add(new RandomSiteLink
                {
                    A = target, B = site.Id,
                    Days = System.Math.Clamp((int)System.Math.Round(distance / PixelsPerDay), 1, MaxDays)
                });
                degrees[target]++;
                degrees[site.Id]++;
            }
            sites.Add(site);
        }

        state.Sites = sites.ToArray();
        state.Links = links.ToArray();
        return state;
    }

    static List<(int Id, double Distance)> Candidates(List<Vector2f> centers, List<int> degrees, Vector2 position) =>
        centers.Select((c, id) => (Id: id, Distance: (double)(c - (Vector2f)position).Length))
            .Where(c => c.Distance <= MaxLength && degrees[c.Id] < MaxNeighbors)
            .OrderBy(c => c.Distance).ToList();

    // Every sand-mask block touched by the area must be free sand.
    static bool OnSand(bool[,] sand, Rect area)
    {
        if (area.Left < 0 || area.Top < 0)
            return false;
        for (int y = area.Top / Block; y <= (area.Top + area.Height - 1) / Block; y++)
            for (int x = area.Left / Block; x <= (area.Left + area.Width - 1) / Block; x++)
                if (y >= sand.GetLength(0) || x >= sand.GetLength(1) || !sand[y, x])
                    return false;
        return true;
    }
    #endregion

    #region Map data
    static readonly ConditionalWeakTable<MapData, MapEntrance[]> baseEntrances = new();
    sealed record WayBase(Way[] Ways, CrossWay[] Cross);
    static readonly ConditionalWeakTable<WayData, WayBase> baseWays = new();
    static readonly ConditionalWeakTable<MapData, List<(Vector2 Position, string File)>> mapIcons = new();

    /// <summary>
    /// Resets the (cached, shared) world map and way data to the files' state and adds the sites of
    /// <paramref name="state"/>. Call with an empty state to remove sites of a previous game.
    /// </summary>
    public static void Apply(MapData map, WayData ways, RandomSiteState state, IReadOnlyList<SiteIcon> icons)
    {
        if (map is null || ways is null)
            return;

        MapEntrance[] entrances = baseEntrances.GetValue(map, m => m.Entrances.ToArray());
        var (baseWayList, baseCross) = baseWays.GetValue(ways, w =>
            new WayBase(w.Ways.ToArray(), w.Cross.Select(c => new CrossWay { Ways = c.Ways?.ToArray() ?? [] }).ToArray()));

        var newEntrances = entrances.ToList();
        var newWays = baseWayList.ToList();
        var cross = baseCross.Select(c => c.Ways.ToList()).ToList();
        var iconList = new List<(Vector2 Position, string File)>();

        foreach (RandomSite site in state.Sites ?? [])
        {
            if (site.Id != newEntrances.Count)
                break; // map files changed since the save; keep the base world
            newEntrances.Add(new MapEntrance { Area = site.Area, TitleId = "=" + site.Name, RoomType = RoomType.Normal });
            if (site.Icon >= 0 && site.Icon < icons.Count)
                iconList.Add((site.Area.Position - icons[site.Icon].Entrance.Position, icons[site.Icon].File));
        }

        while (cross.Count < newEntrances.Count)
            cross.Add([]);
        foreach (RandomSiteLink link in state.Links ?? [])
        {
            if (link.A >= newEntrances.Count || link.B >= newEntrances.Count)
                continue;
            Vector2 a = newEntrances[link.A].Area.Center, b = newEntrances[link.B].Area.Center;
            newWays.Add(new Way
            {
                Start = link.A, End = link.B, Images = [], Points = [a, b],
                Position = new Vector2(System.Math.Min(a.x, b.x), System.Math.Min(a.y, b.y)),
            });
            cross[link.A].Add(newWays.Count - 1);
            cross[link.B].Add(newWays.Count - 1);
        }

        map.Entrances = newEntrances.ToArray();
        ways.Ways = newWays.ToArray();
        ways.Cross = cross.Select(c => new CrossWay { Ways = c.ToArray() }).ToArray();
        mapIcons.AddOrUpdate(map, iconList);
    }

    /// <summary>Site icons on the world map (sprites, map positions).</summary>
    public static IReadOnlyList<(Vector2 Position, ISprite Sprite)> Decorations(MapData map, IResourceManager resources) =>
        mapIcons.TryGetValue(map, out var list)
            ? list.Select(i => (i.Position, resources.GetImage("gfx/sites/" + i.File))).ToList()
            : [];
    #endregion

    #region Game objects
    /// <summary>Creates the locations of a new game's sites and links them to their neighbors.</summary>
    internal static void CreateLocations(ClassicGame game, RandomSiteState state, Burntime.Data.BurnGfx.Save.SaveGame gamdat)
    {
        var container = game.Container;
        var locations = game.World.Locations;
        foreach (RandomSite site in state.Sites ?? [])
        {
            if (site.Id != locations.Count || site.Template < 0 || site.Template >= gamdat.Locations.Length)
                break;
            var template = gamdat.Locations[site.Template];
            int mapNumber = site.Template + 1;
            Map map = container.Create<Map>(new object[] { $"maps/mat_{mapNumber:D3}.burnmap??{mapNumber}" });
            locations += Location.Create(container, new Location.Params(
                Id: site.Id,
                Map: map,
                Water: template.WaterSource,
                WaterReserve: template.Water,
                WaterCapacity: template.WaterCapacity,
                Production: template.Producing == -1 ? null : game.Productions[template.Producing],
                AvailableProducts: (int[])template.Production.Clone(),
                Danger: template.Danger == 0
                    ? null
                    : Interaction.Danger.Instance(template.Danger == 3 ? "radiation" : "gas", template.DangerAmount),
                IsCity: false,
                EntryPoint: template.EntryPoint,
                Ways: [],
                WayLengths: [],
                NeighborIds: []));
        }

        foreach (RandomSiteLink link in state.Links ?? [])
        {
            if (link.A >= locations.Count || link.B >= locations.Count)
                continue;
            Connect(locations[link.A], locations[link.B], link.Days);
            Connect(locations[link.B], locations[link.A], link.Days);
        }
    }

    static void Connect(Location from, Location to, int days)
    {
        if (from.NeighborIds.Contains(to.Id))
            return;
        from.NeighborIds = from.NeighborIds.Append(to.Id).ToArray();
        from.WayLengths = from.WayLengths.Append(days).ToArray();
        from.Neighbors.Add(to);
    }

    /// <summary>Planning input from the current world map and the original locations.</summary>
    internal static MapInfo ReadMapInfo(World world, Burntime.Data.BurnGfx.Save.SaveGame gamdat)
    {
        Rect[] entrances = world.Map.Entrances.Select(e => e.Area).ToArray();
        int[] degrees = Enumerable.Range(0, entrances.Length)
            .Select(i => i < world.Locations.Count ? world.Locations[i].NeighborIds.Length : 0).ToArray();
        var templates = gamdat.Locations.Select((l, id) => LocationTemplate.FromOriginal(id, l)).ToList();
        return new MapInfo(entrances, degrees, ReadSand(), ReadIcons(), templates);
    }

    public static IReadOnlyList<SiteIcon> ReadIcons()
    {
        var icons = new List<SiteIcon>();
        foreach (string[] parts in ReadLines("gfx/sites/sites.txt"))
        {
            int[] numbers = parts.Skip(1).Select(p => int.TryParse(p, out int n) ? n : -1).ToArray();
            if (numbers.Length == 6 && numbers[2] > 0 && numbers[3] > 0 && numbers[4] > 0 && numbers[5] > 0)
                icons.Add(new SiteIcon(parts[0], new Rect(numbers[0], numbers[1], numbers[2], numbers[3]),
                    new Vector2(numbers[4], numbers[5])));
        }
        return icons;
    }

    internal static bool[,] ReadSand()
    {
        var rows = ReadLines("gfx/sites/sand.txt").Select(p => p[0]).ToList();
        int width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var sand = new bool[rows.Count, width];
        for (int y = 0; y < rows.Count; y++)
            for (int x = 0; x < rows[y].Length; x++)
                sand[y, x] = rows[y][x] == '1';
        return sand;
    }

    static IEnumerable<string[]> ReadLines(string path)
    {
        if (!FileSystem.ExistsFile(path))
            yield break;
        File file = FileSystem.GetFile(path);
        for (string? line = file.ReadLine(); line != null; line = file.ReadLine())
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                yield return line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        file.Close();
    }
    #endregion
}

/// <summary>
/// Generated sites of a game, stored in the save. A value type because state objects may only
/// hold value types, arrays, strings and state links; null arrays mean no sites (older saves).
/// </summary>
[Serializable]
public struct RandomSiteState
{
    public int Seed;
    public int FirstId;
    public RandomSite[] Sites;
    public RandomSiteLink[] Links;
}

[Serializable]
public sealed class RandomSite
{
    public int Id;
    public int X, Y, Width, Height;
    public int Icon;
    public int Template;
    public string Name = "";
    public Rect Area => new(X, Y, Width, Height);
}

[Serializable]
public struct RandomSiteLink
{
    public int A, B, Days;
}
