using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Burntime.Remaster.Logic.Generation;

/// <summary>
/// An original location whose map, rooms and environment a generated location reuses.
/// </summary>
public sealed record LocationTemplate(int Id, bool IsCity, int Water, int WaterCapacity, int WaterSource,
    int Danger, int DangerAmount, int[] Production)
{
    public static LocationTemplate FromOriginal(int id, Burntime.Data.BurnGfx.Save.Location location) =>
        new(id, location.IsCity, location.Water, location.WaterCapacity, location.WaterSource,
            location.Danger, location.DangerAmount, location.Production.Where(p => p >= 0).ToArray());
}

/// <summary>
/// Generation parameters. Defaults reproduce the density and travel times of the original world map
/// (37 locations on 448x384 pixels, 47 ways, 1-4 neighbors, about 25 pixels per travel day).
/// </summary>
public sealed record WorldGenSettings
{
    public int LocationCount { get; init; } = 60;
    public double AreaPerLocation { get; init; } = 448.0 * 384.0 / 37.0;
    public double AspectRatio { get; init; } = 448.0 / 384.0;
    public int Margin { get; init; } = 16;
    /// <summary>Minimum distance between locations as a fraction of the average location spacing.</summary>
    public double MinDistanceFactor { get; init; } = 0.6;
    public double WaysPerLocation { get; init; } = 47.0 / 37.0;
    public int MaxNeighbors { get; init; } = 4;
    public double PixelsPerDay { get; init; } = 25;
    public int MaxDays { get; init; } = 4;
    public double CityShare { get; init; } = 5.0 / 37.0;
    public int MinCities { get; init; } = 2;
}

/// <summary>A generated location. <see cref="Name"/> is null when the template's original name is used.</summary>
public sealed record GeneratedLocation(int Id, int X, int Y, LocationTemplate Template, string? Name)
{
    public bool IsCity => Template.IsCity;
}

public readonly record struct GeneratedWay(int A, int B, int Days, double Length);

public sealed record GeneratedWorld(int Seed, int Width, int Height,
    IReadOnlyList<GeneratedLocation> Locations, IReadOnlyList<GeneratedWay> Ways)
{
    public IEnumerable<int> Neighbors(int id) =>
        Ways.Where(w => w.A == id || w.B == id).Select(w => w.A == id ? w.B : w.A);

    public bool IsConnected()
    {
        if (Locations.Count == 0)
            return true;
        var seen = new HashSet<int> { 0 };
        var open = new Stack<int>(seen);
        while (open.Count > 0)
            foreach (int next in Neighbors(open.Pop()))
                if (seen.Add(next))
                    open.Push(next);
        return seen.Count == Locations.Count;
    }

    /// <summary>Stable text form for comparisons, previews and debugging.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"seed {Seed} size {Width}x{Height} locations {Locations.Count} ways {Ways.Count}");
        foreach (var l in Locations)
            text.AppendLine($"L {l.Id} {l.X} {l.Y} template {l.Template.Id} city {(l.IsCity ? 1 : 0)} name {l.Name ?? "-"}");
        foreach (var w in Ways)
            text.AppendLine($"W {w.A} {w.B} days {w.Days}");
        return text.ToString();
    }
}

/// <summary>
/// Creates a random world from a seed: location positions, a connected way network and the
/// original location each generated location is based on. Uses its own random generator so the
/// same seed yields the same world on every platform and .NET version.
/// </summary>
public static class WorldGenerator
{
    public static GeneratedWorld Generate(int seed, WorldGenSettings settings, IReadOnlyList<LocationTemplate> templates)
    {
        var random = new WorldRandom(seed);
        // World maps store at most 255 entrances.
        int count = Math.Clamp(settings.LocationCount, 2, 255);
        if (templates.Count == 0)
            return new GeneratedWorld(seed, 0, 0, [], []);
        double area = settings.AreaPerLocation * count;
        int width = (int)Math.Round(Math.Sqrt(area * settings.AspectRatio));
        int height = (int)Math.Round(Math.Sqrt(area / settings.AspectRatio));

        var points = PlacePoints(random, count, width, height, settings with { LocationCount = count });
        var ways = BuildWays(random, points, settings);
        var cities = ChooseCities(random, points, ways, settings);
        var locations = AssignTemplates(random, points, cities, templates);
        return new GeneratedWorld(seed, width, height, locations, ways);
    }

    // Dart throwing with a shrinking minimum distance: evenly spread, never overlapping.
    static List<(double X, double Y)> PlacePoints(WorldRandom random, int count, int width, int height, WorldGenSettings s)
    {
        var points = new List<(double X, double Y)>();
        double minDistance = s.MinDistanceFactor * Math.Sqrt(s.AreaPerLocation);
        int failures = 0;
        while (points.Count < count)
        {
            double x = s.Margin + random.NextDouble() * (width - 2 * s.Margin);
            double y = s.Margin + random.NextDouble() * (height - 2 * s.Margin);
            if (points.All(p => Distance(p, (x, y)) >= minDistance))
            {
                points.Add((x, y));
                failures = 0;
            }
            else if (++failures > 30 * count)
            {
                minDistance *= 0.95;
                failures = 0;
            }
        }
        return points;
    }

    // Candidate ways form a Gabriel graph (planar, connected, no way passes close to a third location).
    // A minimum spanning tree guarantees connectivity; the shortest remaining candidates add loops.
    static List<GeneratedWay> BuildWays(WorldRandom random, List<(double X, double Y)> p, WorldGenSettings s)
    {
        int n = p.Count;
        var candidates = new List<(int A, int B, double Length, double Order)>();
        for (int a = 0; a < n; a++)
        for (int b = a + 1; b < n; b++)
        {
            var mid = ((p[a].X + p[b].X) / 2, (p[a].Y + p[b].Y) / 2);
            double radius = Distance(p[a], p[b]) / 2;
            bool gabriel = true;
            for (int k = 0; k < n && gabriel; k++)
                if (k != a && k != b && Distance(mid, p[k]) < radius)
                    gabriel = false;
            if (gabriel)
            {
                double length = Distance(p[a], p[b]);
                candidates.Add((a, b, length, length * (1 + 0.3 * random.NextDouble())));
            }
        }

        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        var degree = new int[n];
        var chosen = new List<(int A, int B, double Length)>();
        var used = new HashSet<(int, int)>();

        void Add((int A, int B, double Length, double Order) c)
        {
            chosen.Add((c.A, c.B, c.Length));
            used.Add((c.A, c.B));
            degree[c.A]++;
            degree[c.B]++;
            parent[Find(c.A)] = Find(c.B);
        }

        var byLength = candidates.OrderBy(c => c.Length).ToList();
        // Spanning tree within the neighbor limit; only if that cannot connect everything, exceed it.
        foreach (bool limit in new[] { true, false })
            foreach (var c in byLength)
                if (Find(c.A) != Find(c.B) && (!limit || (degree[c.A] < s.MaxNeighbors && degree[c.B] < s.MaxNeighbors)))
                    Add(c);

        int target = (int)Math.Round(s.WaysPerLocation * n);
        double maxLength = (s.MaxDays + 0.5) * s.PixelsPerDay;
        foreach (var c in candidates.OrderBy(c => c.Order))
        {
            if (chosen.Count >= target)
                break;
            if (!used.Contains((c.A, c.B)) && c.Length <= maxLength &&
                degree[c.A] < s.MaxNeighbors && degree[c.B] < s.MaxNeighbors)
                Add(c);
        }

        return chosen
            .Select(c => new GeneratedWay(c.A, c.B, Math.Clamp((int)Math.Round(c.Length / s.PixelsPerDay), 1, s.MaxDays), c.Length))
            .OrderBy(w => w.A).ThenBy(w => w.B).ToList();
    }

    // Spread cities across the map, preferring well connected locations.
    static HashSet<int> ChooseCities(WorldRandom random, List<(double X, double Y)> p, List<GeneratedWay> ways, WorldGenSettings s)
    {
        int n = p.Count;
        int count = Math.Clamp((int)Math.Round(n * s.CityShare), s.MinCities, n);
        var degree = new int[n];
        foreach (var w in ways) { degree[w.A]++; degree[w.B]++; }

        var cities = new HashSet<int> { random.Next(n) };
        while (cities.Count < count)
        {
            int best = -1;
            double bestScore = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (cities.Contains(i))
                    continue;
                double spacing = cities.Min(c => Distance(p[c], p[i]));
                double score = spacing * (1 + 0.25 * degree[i]) * (0.9 + 0.2 * random.NextDouble());
                if (score > bestScore) { bestScore = score; best = i; }
            }
            cities.Add(best);
        }
        return cities;
    }

    // Each template is used once before any is repeated; repeats get a generated name.
    static List<GeneratedLocation> AssignTemplates(WorldRandom random, List<(double X, double Y)> p,
        HashSet<int> cities, IReadOnlyList<LocationTemplate> templates)
    {
        var deck = new Dictionary<bool, Queue<LocationTemplate>>
        {
            [true] = new(), [false] = new(),
        };
        var usedOnce = new HashSet<int>();
        var names = new NameGenerator(random);
        var result = new List<GeneratedLocation>();
        for (int i = 0; i < p.Count; i++)
        {
            bool city = cities.Contains(i);
            if (deck[city].Count == 0)
            {
                var pool = templates.Where(t => t.IsCity == city).ToList();
                foreach (var t in random.Shuffle(pool.Count > 0 ? pool : templates.ToList()))
                    deck[city].Enqueue(t);
            }
            var template = deck[city].Dequeue();
            string? name = usedOnce.Add(template.Id) ? null : names.Next();
            result.Add(new GeneratedLocation(i, (int)Math.Round(p[i].X), (int)Math.Round(p[i].Y), template, name));
        }
        return result;
    }

    static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    sealed class NameGenerator(WorldRandom random)
    {
        static readonly string[] First =
        [
            "Rust", "Dust", "Bone", "Ash", "Scrap", "Salt", "Iron", "Dead", "Black", "Red",
            "Dry", "Burnt", "Glass", "Stone", "Lead", "Copper", "Sand", "Ghost", "Rot", "Skull",
        ];
        static readonly string[] Second =
        [
            "Creek", "Hill", "Town", "Point", "Flats", "Ridge", "Hollow", "Well", "Pit", "Gate",
            "Camp", "Valley", "Fork", "Mesa", "Springs", "End", "Crossing", "Dunes", "Canyon", "Rock",
        ];
        readonly HashSet<string> used = new();

        public string Next()
        {
            for (int attempt = 0; ; attempt++)
            {
                string name = $"{First[random.Next(First.Length)]} {Second[random.Next(Second.Length)]}";
                if (attempt >= 20)
                    name += $" {attempt - 18}";
                if (used.Add(name))
                    return name;
            }
        }
    }
}

/// <summary>SplitMix64: small, fast and identical on every platform.</summary>
public sealed class WorldRandom(int seed)
{
    ulong state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);

    public ulong NextULong()
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

    public List<T> Shuffle<T>(List<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }
}
