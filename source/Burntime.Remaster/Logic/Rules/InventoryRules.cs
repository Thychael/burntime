using System.Linq;
using Burntime.Remaster.Logic.Generation;

namespace Burntime.Remaster.Logic.Rules;

/// <summary>
/// Extended rules give people a larger inventory: two pages of six slots plus a slot that only
/// holds the active weapon. Classic rules keep the original six slots.
/// </summary>
internal static class InventoryRules
{
    public const int PageSize = 6;
    public const int BagSlots = 2 * PageSize;
    public const int WeaponSlots = 1;
    /// <summary>Original AI bosses carry three more items than human players (9 instead of 6).</summary>
    public const int AiBossBonus = 3;
    const int OriginalCapacity = 6;

    internal static bool Applies(ClassicGame game) => game.Rules == RuleSet.Extended;

    internal static bool IsPerson(Character character) =>
        character.Class is not (CharClass.Dog or CharClass.Mutant or CharClass.Trader);

    /// <summary>
    /// Gives every person without a weapon slot the larger inventory; keeps the original AI boss
    /// bonus. Safe to call repeatedly (new games and loaded older saves).
    /// </summary>
    internal static void Apply(ClassicGame game)
    {
        if (Applies(game))
            Apply(game.World.AllCharacters.ToList());
    }

    internal static void Apply(System.Collections.Generic.IEnumerable<Character> characters)
    {
        foreach (Character character in characters)
        {
            if (!IsPerson(character) || character.HasWeaponSlot || character.Items == null)
                continue;
            int bonus = System.Math.Max(0, character.Items.MaxCount - OriginalCapacity);
            character.Items.MaxCount = BagSlots + WeaponSlots + bonus;
            character.HasWeaponSlot = true;
        }
    }
}
