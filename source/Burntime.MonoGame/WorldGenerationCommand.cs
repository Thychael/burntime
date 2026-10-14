using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Burntime.Framework;
using Burntime.Platform.IO;
using Burntime.Remaster.Logic.Generation;

namespace Burntime.MonoGame;

/// <summary>
/// Generates a random world without starting the game and writes its description:
/// <c>--generate-world [--seed N] [--locations N] [--out file.txt]</c>.
/// </summary>
internal static class WorldGenerationCommand
{
    public static bool IsRequested(string[] args) => args.Contains("--generate-world", StringComparer.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        int seed = 1;
        int locations = new WorldGenSettings().LocationCount;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            string value = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i].ToLowerInvariant())
            {
                case "--seed": seed = int.Parse(value, CultureInfo.InvariantCulture); i++; break;
                case "--locations": locations = int.Parse(value, CultureInfo.InvariantCulture); i++; break;
                case "--out": output = value; i++; break;
            }
        }

        FileSystem.BasePath = AppContext.BaseDirectory;
        new PackageManager("game/").LoadPackages("classic", FileSystem.VFS, null);
        var gamdat = new Burntime.Data.BurnGfx.Save.SaveGame();
        if (!gamdat.Open())
        {
            Console.Error.WriteLine("GAM.DAT not found.");
            return 1;
        }

        var templates = gamdat.Locations.Select((location, id) => LocationTemplate.FromOriginal(id, location)).ToList();
        var world = WorldGenerator.Generate(seed, new WorldGenSettings { LocationCount = locations }, templates);
        string text = world.Describe();
        if (output is null)
        {
            Console.Write(text);
        }
        else
        {
            string fullPath = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllText(fullPath, text);
            Console.WriteLine($"World written to {fullPath}");
        }
        return 0;
    }
}
