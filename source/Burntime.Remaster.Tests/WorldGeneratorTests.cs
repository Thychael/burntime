using System;
using System.Collections.Generic;
using System.Linq;
using Burntime.Remaster.Logic.Generation;

namespace Burntime.Remaster.Tests;

using static Program;

static class WorldGeneratorTests
{
    // The 37 original locations as stored in GAM.DAT: id, city, water, capacity, source, danger, amount, production.
    internal static readonly LocationTemplate[] OriginalTemplates =
    [
        T(0, true, 0, 0, 0, 0, 0), T(1, false, 8, 8, 5, 0, 0, 0, 1), T(2, false, 10, 10, 4, 7, 95, 0),
        T(3, false, 6, 6, 5, 0, 0, 0, 1, 2, 3), T(4, false, 0, 6, 5, 3, 45, 0, 1, 2), T(5, false, 0, 8, 6, 0, 0, 0, 1),
        T(6, false, 2, 7, 5, 0, 0, 0, 1, 2), T(7, false, 6, 6, 1, 0, 0, 0, 1, 2), T(8, true, 0, 0, 0, 0, 0),
        T(9, false, 5, 5, 1, 0, 0, 0, 1), T(10, false, 2, 6, 7, 0, 0, 0, 1, 2, 3), T(11, false, 6, 6, 7, 0, 0, 0, 1, 2, 3),
        T(12, false, 8, 8, 6, 0, 0, 0, 1, 2), T(13, true, 0, 0, 0, 0, 0), T(14, false, 5, 5, 6, 7, 95, 0, 1),
        T(15, false, 2, 5, 6, 0, 0, 0, 1, 2, 3), T(16, false, 20, 20, 10, 0, 0, 0, 1, 2), T(17, false, 2, 5, 4, 0, 0, 0, 1),
        T(18, true, 0, 0, 0, 0, 0), T(19, false, 5, 5, 1, 0, 0, 0, 1), T(20, false, 5, 5, 5, 0, 0, 0, 1, 2),
        T(21, true, 0, 0, 0, 0, 0), T(22, false, 0, 0, 0, 3, 127), T(23, false, 4, 4, 3, 0, 0, 0, 1),
        T(24, false, 5, 5, 7, 0, 0, 0, 1, 2, 3), T(25, false, 20, 20, 5, 0, 0), T(26, false, 6, 6, 1, 0, 0, 0, 1, 2),
        T(27, false, 0, 0, 0, 3, 87), T(28, false, 10, 10, 7, 0, 0, 0), T(29, false, 20, 20, 6, 0, 0, 0, 1, 2),
        T(30, false, 8, 8, 7, 0, 0, 0, 1, 2, 3), T(31, false, 5, 5, 4, 0, 0, 0, 1), T(32, false, 2, 4, 4, 0, 0, 0, 1, 2),
        T(33, false, 9, 9, 6, 0, 0, 0, 1, 2), T(34, false, 4, 4, 8, 0, 0, 0, 1, 2, 3), T(35, false, 15, 15, 6, 0, 0, 0, 1, 2, 3),
        T(36, false, 4, 4, 3, 0, 0, 0, 1),
    ];

    static LocationTemplate T(int id, bool city, int water, int capacity, int source, int danger, int amount,
        params int[] production) => new(id, city, water, capacity, source, danger, amount, production);

    static string Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text.Replace("\r", ""))
            hash = (hash ^ c) * 1099511628211UL;
        return hash.ToString("x16");
    }

    static GeneratedWorld World(int seed, int count) =>
        WorldGenerator.Generate(seed, new WorldGenSettings { LocationCount = count }, OriginalTemplates);

    internal static IEnumerable<Case<int>> Cases()
    {
        yield return Int("same seed, same world", 0, () =>
        {
            Equal(World(42, 60).Describe(), World(42, 60).Describe(), "description");
            return 0;
        });

        yield return Int("different seeds, different worlds", 0, () =>
        {
            Equal(true, World(1, 60).Describe() != World(2, 60).Describe(), "worlds differ");
            return 0;
        });

        yield return Int("seed 42 is stable across versions", 0, () =>
        {
            // Changing the generator changes existing worlds; update deliberately when that is intended.
            var world = World(42, 60);
            Equal("seed 42 size 570x489 locations 60 ways 76", world.Describe().Split('\n')[0].TrimEnd(), "summary");
            Equal("cb80d2bc4cbed773", Fnv1a(world.Describe()), "fingerprint of the whole world");
            return 0;
        });

        foreach (int count in new[] { 2, 10, 37, 60, 120, 255 })
        foreach (int seed in new[] { 1, 7, 42, 1234, -5 })
            yield return Int($"valid world, {count} locations, seed {seed}", 0, () =>
            {
                var world = World(seed, count);
                var settings = new WorldGenSettings();
                Equal(count, world.Locations.Count, "location count");
                Equal(true, world.IsConnected(), "every location reachable");

                foreach (var l in world.Locations)
                {
                    Equal(true, l.X >= settings.Margin && l.X <= world.Width - settings.Margin, $"x of {l.Id} inside map");
                    Equal(true, l.Y >= settings.Margin && l.Y <= world.Height - settings.Margin, $"y of {l.Id} inside map");
                    int degree = world.Neighbors(l.Id).Count();
                    Equal(true, degree >= 1 && degree <= settings.MaxNeighbors, $"neighbors of {l.Id} ({degree})");
                }

                Equal(world.Ways.Count, world.Ways.Select(w => (w.A, w.B)).Distinct().Count(), "no duplicate ways");
                foreach (var w in world.Ways)
                {
                    Equal(true, w.A < w.B, "ways are stored once, ordered");
                    Equal(true, w.Days >= 1 && w.Days <= settings.MaxDays, $"travel days {w.A}-{w.B} ({w.Days})");
                }

                double minimum = world.Locations.SelectMany(a => world.Locations.Where(b => b.Id > a.Id)
                    .Select(b => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)))).DefaultIfEmpty(99).Min();
                Equal(true, minimum >= 15, $"locations do not overlap (closest {minimum:0})");

                int cities = world.Locations.Count(l => l.IsCity);
                int expectedCities = Math.Clamp((int)Math.Round(count * settings.CityShare), settings.MinCities, count);
                Equal(expectedCities, cities, "city count");
                return 0;
            });

        yield return Int("templates are used once before repeating", 0, () =>
        {
            var world = World(3, 120);
            var cities = world.Locations.Where(l => l.IsCity).ToList();
            var others = world.Locations.Where(l => !l.IsCity).ToList();
            // 32 non-city templates: the first 32 non-city locations use each one exactly once.
            Equal(32, others.Take(32).Select(l => l.Template.Id).Distinct().Count(), "first non-city round");
            Equal(5, cities.Take(5).Select(l => l.Template.Id).Distinct().Count(), "first city round");
            Equal(true, world.Locations.All(l => l.IsCity == l.Template.IsCity), "cities use city templates");
            return 0;
        });

        yield return Int("repeated templates get unique generated names", 0, () =>
        {
            var world = World(3, 120);
            var named = world.Locations.Where(l => l.Name != null).ToList();
            Equal(120 - 37, named.Count, "generated names for repeats");
            Equal(named.Count, named.Select(l => l.Name).Distinct().Count(), "names are unique");
            Equal(37, world.Locations.Where(l => l.Name == null).Select(l => l.Template.Id).Distinct().Count(),
                "original names used once each");
            return 0;
        });

        yield return Int("network resembles the original", 0, () =>
        {
            // Original: 47 ways for 37 locations, travel days mostly 2-3.
            var worlds = Enumerable.Range(0, 20).Select(seed => World(seed, 37)).ToList();
            double ways = worlds.Average(w => w.Ways.Count);
            Equal(true, ways >= 44 && ways <= 48, $"average way count ({ways:0.0})");
            var days = worlds.SelectMany(w => w.Ways).Select(w => w.Days).ToList();
            double shortOrMedium = days.Count(d => d is 2 or 3) / (double)days.Count;
            Equal(true, shortOrMedium >= 0.6, $"share of 2-3 day ways ({shortOrMedium:0.00})");
            return 0;
        });

        yield return Int("graceful without templates", 0, () =>
        {
            var world = WorldGenerator.Generate(1, new WorldGenSettings(), []);
            Equal(0, world.Locations.Count, "empty world");
            return 0;
        });

        yield return Int("location count clamped to 255", 255, () =>
            WorldGenerator.Generate(1, new WorldGenSettings { LocationCount = 400 }, OriginalTemplates).Locations.Count);
    }
}
