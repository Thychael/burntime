using System;
using System.Collections.Generic;
using System.Linq;
using Burntime.Data.BurnGfx;
using Burntime.Platform;
using Burntime.Remaster.Logic.Generation;

namespace Burntime.Remaster.Tests;

using static Program;

static class RandomSitesTests
{
    // Entrances of the remake's world map (MAT_000.burnmap): 37 original locations and New Village.
    static readonly Rect[] WorldEntrances =
    [
        new(60, 305, 25, 14), new(142, 313, 17, 10), new(256, 344, 19, 11), new(52, 349, 19, 13),
        new(10, 288, 19, 9), new(275, 279, 20, 11), new(160, 254, 26, 9), new(93, 268, 15, 9),
        new(380, 285, 30, 14), new(403, 360, 16, 12), new(344, 312, 21, 11), new(13, 245, 9, 7),
        new(112, 234, 15, 8), new(269, 186, 28, 14), new(229, 221, 21, 11), new(336, 214, 11, 6),
        new(134, 181, 14, 18), new(39, 205, 19, 12), new(20, 151, 23, 13), new(193, 190, 10, 7),
        new(202, 156, 16, 13), new(111, 60, 26, 13), new(401, 194, 17, 11), new(89, 137, 18, 14),
        new(5, 119, 16, 11), new(44, 61, 20, 8), new(264, 95, 17, 10), new(145, 95, 17, 12),
        new(302, 61, 11, 8), new(185, 69, 22, 12), new(104, 118, 12, 9), new(214, 112, 21, 12),
        new(88, 107, 9, 9), new(267, 136, 21, 11), new(345, 154, 21, 12), new(409, 67, 17, 9),
        new(357, 98, 21, 12), new(443, 235, 19, 13),
    ];

    static RandomSites.MapInfo Info() => new(WorldEntrances,
        WorldEntrances.Select((_, i) => i % 3 == 0 ? 3 : 2).ToArray(),
        RandomSites.ReadSand(), RandomSites.ReadIcons(), WorldGeneratorTests.OriginalTemplates);

    static string Describe(RandomSiteState state) =>
        string.Join(";", state.Sites.Select(s => $"{s.Id}:{s.X},{s.Y},{s.Icon},{s.Template},{s.Name}")) + "|" +
        string.Join(";", state.Links.Select(l => $"{l.A}-{l.B}/{l.Days}"));

    internal static IEnumerable<Case<int>> Cases()
    {
        yield return Int("site data files are readable", 0, () =>
        {
            Equal(11, RandomSites.ReadIcons().Count, "site icons");
            bool[,] sand = RandomSites.ReadSand();
            Equal(48, sand.GetLength(0), "sand rows");
            Equal(56, sand.GetLength(1), "sand columns");
            return 0;
        });

        yield return Int("same seed, same sites", 0, () =>
        {
            Equal(Describe(RandomSites.Plan(5, 10, Info())), Describe(RandomSites.Plan(5, 10, Info())), "plan");
            Equal(true, Describe(RandomSites.Plan(5, 10, Info())) != Describe(RandomSites.Plan(6, 10, Info())),
                "different seeds differ");
            return 0;
        });

        foreach (int seed in new[] { 1, 2, 3, 42, 1234, int.MaxValue })
            yield return Int($"valid sites, seed {seed}", 0, () =>
            {
                var info = Info();
                var state = RandomSites.Plan(seed, RandomSites.DefaultCount, info);
                Equal(RandomSites.DefaultCount, state.Sites.Length, "site count");
                Equal(WorldEntrances.Length, state.FirstId, "first site id");

                var centers = WorldEntrances.Select(e => (Vector2f)e.Center).ToList();
                var degrees = info.Degrees.ToList();
                for (int k = 0; k < state.Sites.Length; k++)
                {
                    var site = state.Sites[k];
                    Equal(state.FirstId + k, site.Id, "consecutive ids");
                    Vector2f center = site.Area.Center;
                    double closest = centers.Min(c => (c - center).Length);
                    Equal(true, closest >= 36, $"site {site.Id} keeps its distance ({closest:0})");
                    for (int y = site.Y / 8; y <= (site.Y + site.Height - 1) / 8; y++)
                        for (int x = site.X / 8; x <= (site.X + site.Width - 1) / 8; x++)
                            Equal(true, info.Sand[y, x], $"site {site.Id} entrance on sand");
                    Equal(false, info.Templates[site.Template].IsCity, $"site {site.Id} uses a non-city template");
                    Equal(true, !string.IsNullOrEmpty(site.Name), "site name");
                    centers.Add(center);
                    degrees.Add(0);
                }

                foreach (var site in state.Sites)
                {
                    var own = state.Links.Where(l => l.B == site.Id).ToList();
                    Equal(true, own.Count is 1 or 2, $"site {site.Id} has 1-2 ways ({own.Count})");
                    Equal(true, own.All(l => l.A < site.Id), $"site {site.Id} links to earlier locations (reachable)");
                }
                foreach (var link in state.Links)
                {
                    Equal(true, link.Days is >= 1 and <= 4, $"travel days {link.A}-{link.B}");
                    degrees[link.A]++;
                    degrees[link.B]++;
                }
                Equal(true, degrees.All(d => d <= 4), "at most four neighbors");
                Equal(state.Sites.Length, state.Sites.Select(s => s.Name).Distinct().Count(), "unique names");
                return 0;
            });

        yield return Int("no sites without count", 0, () =>
        {
            var state = RandomSites.Plan(1, 0, Info());
            Equal(0, state.Sites.Length, "sites");
            Equal(0, state.Links.Length, "links");
            return 0;
        });

        yield return Int("apply adds sites and resets shared map data", 0, () =>
        {
            var icons = RandomSites.ReadIcons();
            var map = new MapData { Entrances = WorldEntrances.Select(e => new MapEntrance { Area = e, TitleId = "burn?0" }).ToArray() };
            var ways = new WayData
            {
                Ways = [new Way { Start = 0, End = 1, Images = [] }],
                Cross = WorldEntrances.Select((_, i) => new CrossWay { Ways = i < 2 ? [0] : [] }).ToArray(),
            };
            var state = RandomSites.Plan(9, 10, Info());

            RandomSites.Apply(map, ways, state, icons);
            Equal(48, map.Entrances.Length, "entrances with sites");
            Equal(1 + state.Links.Length, ways.Ways.Length, "ways with sites");
            Equal(48, ways.Cross.Length, "cross ways for every entrance");
            Equal("=" + state.Sites[0].Name, map.Entrances[38].TitleId, "literal site title");
            var link = state.Links[0];
            Equal(true, ways.Cross[link.A].Ways.Contains(1) && ways.Cross[link.B].Ways.Contains(1), "first site way is linked at both ends");
            Equal(2, ways.Ways[1].Points.Length, "site ways are drawn as lines");

            RandomSites.Apply(map, ways, state, icons);
            Equal(48, map.Entrances.Length, "applying twice does not duplicate");

            RandomSites.Apply(map, ways, default, icons);
            Equal(38, map.Entrances.Length, "empty state restores the base map");
            Equal(1, ways.Ways.Length, "empty state restores the base ways");
            Equal(1, ways.Cross[0].Ways.Length, "base cross ways");
            return 0;
        });

        yield return Int("apply ignores sites from a different map", 38, () =>
        {
            var map = new MapData { Entrances = WorldEntrances.Take(37).Select(e => new MapEntrance { Area = e }).ToArray() };
            var ways = new WayData { Ways = [], Cross = new CrossWay[37] };
            RandomSites.Apply(map, ways, RandomSites.Plan(9, 10, Info()), RandomSites.ReadIcons());
            return map.Entrances.Length + 1;
        });
    }
}
