using System;
using System.Collections.Generic;
using System.Linq;
using Burntime.Framework.States;
using Burntime.Remaster.Logic;
using Burntime.Remaster.Logic.Rules;

namespace Burntime.Remaster.Tests;

using static Program;

static class InventoryRulesTests
{
    static Item TypedItem(StateManager manager, string id, params string[] classes)
    {
        var data = new Burntime.Remaster.Logic.Data.ItemTypeData
        {
            DataName = id, DamageValues = Array.Empty<int>(), Class = classes, Protection = Array.Empty<string>()
        };
        return manager.Create<Item>(manager.Create<ItemType>(data));
    }

    static (StateManager Manager, Character Person) Person(int maxCount = 6, CharClass kind = CharClass.Mercenary)
    {
        var manager = new StateManager(null!);
        var person = manager.Create(() => new Character());
        person.Class = kind;
        person.Items = manager.Create<ItemList>();
        person.Items.MaxCount = maxCount;
        return (manager, person);
    }

    static void Fill(StateManager manager, Character person, int count, string id = "item_meat")
    {
        for (int i = 0; i < count; i++)
            person.Items.Add(TypedItem(manager, id));
    }

    internal static IEnumerable<Case<int>> Cases()
    {
        yield return Int("people get twelve bag slots and a weapon slot", 13, () =>
        {
            var (_, person) = Person();
            InventoryRules.Apply([person]);
            Equal(true, person.HasWeaponSlot, "weapon slot");
            Equal(12, person.BagCapacity, "bag capacity");
            return person.Items.MaxCount;
        });

        yield return Int("AI bosses keep their three extra slots", 16, () =>
        {
            var (_, boss) = Person(9, CharClass.Boss);
            InventoryRules.Apply([boss]);
            return boss.Items.MaxCount;
        });

        foreach (CharClass kind in new[] { CharClass.Dog, CharClass.Mutant, CharClass.Trader })
            yield return Int($"{kind} inventory unchanged", 6, () =>
            {
                var (_, other) = Person(6, kind);
                InventoryRules.Apply([other]);
                Equal(false, other.HasWeaponSlot, "no weapon slot");
                return other.Items.MaxCount;
            });

        yield return Int("applying twice changes nothing", 13, () =>
        {
            var (_, person) = Person();
            InventoryRules.Apply([person]);
            InventoryRules.Apply([person]);
            return person.Items.MaxCount;
        });

        yield return Int("the extra slot only takes a weapon", 0, () =>
        {
            var (manager, person) = Person();
            InventoryRules.Apply([person]);
            Fill(manager, person, 12);
            Equal(false, person.CanTake(TypedItem(manager, "item_rags")), "13th non-weapon");
            Equal(0, person.FreeSlots, "free slots for any item");
            Item knife = TypedItem(manager, "item_knife", "weapon");
            Equal(true, person.CanTake(knife), "weapon fits the weapon slot");
            person.Items.Add(knife);
            Equal(knife, person.SlotWeapon, "weapon shown in the weapon slot");
            Equal(false, person.CanTake(TypedItem(manager, "item_axe", "weapon")), "full");
            return 0;
        });

        yield return Int("active weapon is shown in the weapon slot", 0, () =>
        {
            var (manager, person) = Person();
            InventoryRules.Apply([person]);
            Fill(manager, person, 3);
            Equal<Item?>(null, person.SlotWeapon, "no weapon");
            Item knife = TypedItem(manager, "item_knife", "weapon");
            person.Items.Add(knife);
            Equal<Item?>(null, person.SlotWeapon, "carried but not selected");
            person.SelectItem(knife);
            Equal(knife, person.SlotWeapon, "selected weapon");
            Equal(9, person.FreeSlots, "bag slots left");
            return 0;
        });

        yield return Int("taking items respects the weapon slot", 0, () =>
        {
            var (manager, person) = Person();
            InventoryRules.Apply([person]);
            Fill(manager, person, 11);
            var source = manager.Create<ItemList>();
            source.Add(TypedItem(manager, "item_rags"));
            source.Add(TypedItem(manager, "item_tin"));
            source.Add(TypedItem(manager, "item_knife", "weapon"));
            person.TakeItems(source);
            Equal(13, person.Items.Count, "took one item and the weapon");
            Equal(1, source.Count, "one non-weapon item stays");
            Equal("item_tin", source[0].Type.ID, "the item that did not fit");
            return 0;
        });

        yield return Int("without weapon slot the original rule applies", 0, () =>
        {
            var (manager, person) = Person();
            Fill(manager, person, 5);
            Equal(true, person.CanTake(TypedItem(manager, "item_rags")), "sixth item");
            Fill(manager, person, 1);
            Equal(false, person.CanTake(TypedItem(manager, "item_rags")), "seventh item");
            Equal(6, person.BagCapacity, "bag capacity");
            Equal<Item?>(null, person.SlotWeapon, "no weapon slot");
            return 0;
        });
    }
}
