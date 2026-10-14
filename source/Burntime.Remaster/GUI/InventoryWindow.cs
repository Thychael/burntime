using System;
using System.Collections.Generic;
using System.Linq;
using Burntime.Remaster.Logic;
using Burntime.Remaster.Logic.Rules;
using Burntime.Framework;
using Burntime.Framework.GUI;
using Burntime.Platform;
using Burntime.Platform.Graphics;

namespace Burntime.Remaster.GUI
{
    enum InventorySide
    {
        Left,
        Right,
        None
    }

    class InventoryWindow : Container
    {
        sealed class InventoryPage
        {
            public Character Character;
            public int Offset = 0;
            public int PageNumber = 0;

            public InventoryPage(Character character, int pageNumber)
            {
                Character = character;
                PageNumber = pageNumber;
            }
        }

        GuiImage back;
        List<InventoryPage> pages;
        int activePageIndex;
        InventoryPage activePage;
        Vector2 basePos;
        ICharacterCollection group;
        Character leader;
        FaceWindow face;
        GuiFont font;
        GuiFont smallFont;
        bool side;
        Vector2? touchSwipeOrigin;
        float touchSwipeResetRemaining;

        const float TouchSwipeResetDelay = 0.15f;

        GuiFont nameFont;
        String pageName;
        Vector2 namePosition;

        Button[] pageButtons = new Button[5];
        int[] pageIndices = new int[5];

        ItemGridWindow grid;
        public ItemGridWindow Grid
        {
            get { return grid; }
            set { grid = value; }
        }

        // Extended rules: weapon slot in the gap below the portrait, above the first item column,
        // and two pages of bag slots (left side only).
        static readonly Vector2 WeaponSlotPosition = new(22, 56);
        const int WeaponTextX = 52;
        readonly WeaponSlotWindow? weaponSlot;
        readonly Button[] bagPageButtons = new Button[2];
        int bagPage;
        int bagPageCount = 1;

        bool ShowsWeaponSlot => !side && activePage != null && activePage.Character.HasWeaponSlot;

        public Character ActiveCharacter
        {
            get { if (activePage == null) return null; return activePage.Character; }
        }

        public int PageCount => pages.Count;

        public bool SelectAdjacentPage(int direction)
        {
            if (pages.Count <= 1)
                return false;

            int nextPage = (activePageIndex + direction + pages.Count) % pages.Count;
            for (int i = 0; i < pages.Count; i++)
            {
                if (pageIndices[i] == nextPage)
                {
                    OnPage(i);
                    grid.ResetFocus();
                    return true;
                }
            }

            return false;
        }

        public override bool OnTouchScroll(Vector2 position, Vector2 delta)
        {
            if (System.Math.Abs(delta.y) <= System.Math.Abs(delta.x))
                return false;

            // Drag gestures arrive as a stream of deltas. Page once per swipe,
            // even when the finger crosses several rows of the inventory.
            bool alreadyHandled = touchSwipeOrigin.HasValue && touchSwipeOrigin.Value == position;
            touchSwipeOrigin = position;
            touchSwipeResetRemaining = TouchSwipeResetDelay;
            if (alreadyHandled)
                return true;

            // Content follows the finger: swiping up advances, swiping down goes back.
            int direction = delta.y < 0 ? 1 : -1;
            Vector2? sourcePosition = grid.FocusPosition;
            if (SelectAdjacentPage(direction) && sourcePosition.HasValue)
                grid.FocusPageEdge(new Vector2(0, direction), sourcePosition.Value);
            return true;
        }

        public override void OnUpdate(float elapsed)
        {
            // Delayed loading can leave the constructor with zero background dimensions.
            // Keep touch bounds aligned with the loaded panel, including graphics changes.
            if (back.IsLoaded)
                Size = back.Size + basePos;

            if (touchSwipeOrigin.HasValue && (touchSwipeResetRemaining -= elapsed) <= 0)
                touchSwipeOrigin = null;
            base.OnUpdate(elapsed);
        }

        public LogicEvent LeftClickItemEvent;
        public LogicEvent RightClickItemEvent;

        public InventoryWindow(Module App, InventorySide Side)
            : base(App)
        {
            side = (Side == InventorySide.Right);

            back = side ? "inv.raw?2" : "gfx/inventory_left.png";

            basePos = new Vector2(15, 15);
            Size = new Vector2(back.Width, back.Height) + basePos;

            face = new FaceWindow(App);
            face.Position = side ? (basePos + new Vector2(67, 0)) : basePos;
            face.DisplayOnly = true;
            face.Layer = this.Layer + 6;
            Windows += face;

            font = new GuiFont(BurntimeClassic.FontName, ClassicColors.InventoryText);
            font.Borders = TextBorders.Screen;
            smallFont = new GuiFont("font-small.txt", ClassicColors.InventoryText);
            smallFont.Borders = TextBorders.Screen;
            nameFont = new GuiFont(BurntimeClassic.FontName, ClassicColors.MenuTextHover);
            nameFont.Borders = TextBorders.Screen;

            pageName = "";

            for (int i = 0; i < Logic.Group.MAX_PEOPLE; i++)
            {
                pageButtons[i] = new Button(App);
                pageButtons[i].Image = "munt.raw?" + (5 + i);
                if (side)
                    pageButtons[i].Position = basePos + new Vector2(120 + 3 * i, 142 - 18 * i);
                else
                    pageButtons[i].Position = basePos + new Vector2(-3 * i, 142 - 18 * i);
                pageButtons[i].Hide();
                pageButtons[i].Command += new CommandHandler(OnPage, i);
                pageIndices[i] = i;
                pageButtons[i].Layer = Layer + Logic.Group.MAX_PEOPLE + 1;
                Windows += pageButtons[i];
            }

            Windows += grid = new ItemGridWindow(App)
            {
                UnifiedSelection = true,
                LockPositions = true,
                Position = new Vector2(side ? 9 : 19, side ? 72 : 83) + basePos,
                Spacing = new Vector2(4, side ? 16 : 5),
                Grid = new Vector2(3, 2),
                Layer = Layer + Logic.Group.MAX_PEOPLE + 1
            };
            grid.LeftClickItemEvent += OnLeftClickItem;
            grid.RightClickItemEvent += OnRightClickItem;

            if (!side)
            {
                Windows += weaponSlot = new WeaponSlotWindow(App)
                {
                    Position = basePos + WeaponSlotPosition,
                    Layer = Layer + Logic.Group.MAX_PEOPLE + 1
                };
                weaponSlot.LeftClick += item => OnLeftClickItem(item);
                weaponSlot.RightClick += item => OnRightClickItem(item);
                weaponSlot.Hide();

                string[] arrows = ["gfx/ui/inventory_page_up.png", "gfx/ui/inventory_page_down.png"];
                for (int i = 0; i < bagPageButtons.Length; i++)
                {
                    bagPageButtons[i] = new Button(App)
                    {
                        Image = arrows[i],
                        Position = basePos + new Vector2(125, i == 0 ? 85 : 134),
                        Layer = Layer + Logic.Group.MAX_PEOPLE + 2
                    };
                    bagPageButtons[i].Command += new CommandHandler(OnBagPage, i);
                    bagPageButtons[i].Hide();
                    Windows += bagPageButtons[i];
                }
            }

            pages = new List<InventoryPage>();
            activePageIndex = 0;
            activePage = null;
        }

        public void SetGroup(Character leader)
        {
            this.group = leader.GetGroup();
            this.leader = leader;
            touchSwipeOrigin = null;

            activePageIndex = 0;

            Refresh();
        }

        public int FreeSlots
        {
            get { return group.GetFreeSlotCount(); }
        }

        void Refresh()
        {
            lock (this)
            {
                //activePageIndex = 0;
                pages.Clear();
                for (int i = 0; i < pageIndices.Length; i++)
                    pageIndices[i] = i;

                // trader group
                if (group[0].Class == CharClass.Trader)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        InventoryPage page = new InventoryPage(group[0], i);
                        page.Offset = i * 6;
                        pages.Add(page);
                        pageButtons[i].Show();
                    }
                    for (int i = 2; i < pageButtons.Length; i++)
                        pageButtons[i].Hide();
                }
                // most likely player group
                else
                {
                    int count = 0;

                    for (int i = 0; i < group.Count; i++)
                    {
                        // should not happen but well
                        if (count >= pageButtons.Length)
                            continue;

                        // show only if in range
                        if (group.IsInRange(leader, group[i]))
                        {
                            InventoryPage page = new InventoryPage(group[i], i);
                            pages.Add(page);
                            pageButtons[count].Image = "munt.raw?" + (5 + i);
                            pageButtons[count].Show();

                            count++;
                        }
                    }
                    for (int i = count; i < pageButtons.Length; i++)
                        pageButtons[i].Hide();
                }
                face.FaceID = pages[activePageIndex].Character.FaceID;
                activePage = pages[activePageIndex];

                OnSelectPage();
            }
        }

        public void OnSelectPage()
        {
            grid.Clear();

            if (activePage != null)
            {
                RefreshCombatLoadout();
                Character character = activePage.Character;
                bool slot = ShowsWeaponSlot;
                Item? slotItem = slot ? character.SlotWeapon : null;
                var bag = character.Items.Where(item => item != slotItem).ToList();

                bagPageCount = slot ? System.Math.Max(1, (character.BagCapacity + InventoryRules.PageSize - 1) / InventoryRules.PageSize) : 1;
                bagPage = System.Math.Clamp(bagPage, 0, bagPageCount - 1);
                for (int i = activePage.Offset + bagPage * InventoryRules.PageSize; i < bag.Count && grid.Count < InventoryRules.PageSize; i++)
                    grid.Add(bag[i]);

                if (weaponSlot != null)
                {
                    weaponSlot.Item = slotItem;
                    weaponSlot.IsVisible = slot;
                }
                for (int i = 0; i < bagPageButtons.Length; i++)
                    bagPageButtons[i]?.Hide();
                if (bagPageCount > 1)
                {
                    // show the arrow that leads to the other page
                    bagPageButtons[bagPage == 0 ? 1 : 0].Show();
                }

                face.FaceID = character.FaceID;
                RefreshItemMarkers();
            }
        }

        /// <summary>Rebuilds the displayed items after the active character's inventory changed.</summary>
        public void RefreshItems() => OnSelectPage();

        void OnBagPage(int index)
        {
            int target = index == 0 ? bagPage - 1 : bagPage + 1;
            if (target < 0 || target >= bagPageCount)
                return;
            bagPage = target;
            OnSelectPage();
        }

        public override bool OnMouseWheel(Vector2 position, int delta)
        {
            if (bagPageCount <= 1)
                return false;
            OnBagPage(delta > 0 ? 0 : 1);
            return true;
        }

        public void RefreshCombatLoadout()
        {
            if (activePage == null)
                return;

            ((ClassicGame)activePage.Character.Container.Root).RuleBook
                .SelectCombatLoadout(activePage.Character);
            RefreshItemMarkers();
        }

        public void RefreshItemMarkers()
        {
            grid.Selection.Clear();
            if (activePage == null)
                return;

            foreach (Item item in activePage.Character.Items)
                if (activePage.Character.IsItemInUse(item))
                    grid.Selection.Add(item);
        }

        public override void OnRender(RenderTarget Target)
        {
            var inventoryOffset = side ? new Vector2(3, -2) : new Vector2(-3, -3);

            for (int i = pages.Count - 1; i >= 0; i--)
            {
                Target.DrawSprite(basePos + inventoryOffset * i, back);
                Target.Layer++;
            }


            TextHelper txt = new TextHelper(app, "burn");

            txt.AddArgument("|B", activePage.Character.Health);
            txt.AddArgument("|A", activePage.Character.Experience);
            txt.AddArgument("|C", activePage.Character.Water);
            txt.AddArgument("|D", activePage.Character.Food);

            var combat = ((ClassicGame)activePage.Character.Container.Root).RuleBook
                .GetEquippedCombatPreview(activePage.Character);
            txt.AddArgument("{defense}", combat.Defense ?? 0);
            string damage = combat.Minimum == combat.Maximum
                ? combat.Minimum.ToString() : $"{combat.Minimum}-{combat.Maximum}";
            txt.AddArgument("{damage}", damage);

            int fontSpacing = 10;

            Vector2 textPos = new Vector2();
            textPos.x = basePos.x + (side ? 11 : 73);
            textPos.y = basePos.y + 11;
            font.DrawText(Target, textPos, txt[40 + (int)activePage.Character.Class], TextAlignment.Left, VerticalTextAlignment.Top);
            textPos.y += fontSpacing;
            font.DrawText(Target, textPos, txt[380], TextAlignment.Left, VerticalTextAlignment.Top);
            textPos.y += fontSpacing;
            font.DrawText(Target, textPos, txt[381], TextAlignment.Left, VerticalTextAlignment.Top);
            textPos.y += fontSpacing;

            if (activePage.Character.Class != CharClass.Trader)
            {
                string food = txt[403];
                string water = txt[402];
                if (!app.IsNewGfx && app.Language == "de")
                {
                    food = food.Replace(" T", "T");
                    water = water.Replace(" T", "T");
                }
                font.DrawText(Target, textPos, food, TextAlignment.Left, VerticalTextAlignment.Top);
                textPos.y += fontSpacing;
                font.DrawText(Target, textPos, water, TextAlignment.Left, VerticalTextAlignment.Top);
                textPos.y += fontSpacing;

                GuiFont statsFont = app.IsNewGfx ? smallFont : font;
                string hazards = "";
                foreach (string hazard in new[] { "gas", "radiation" })
                {
                    float rate = activePage.Character.GetHazardProtectionRate(hazard);
                    if (rate <= 0)
                        continue;
                    int label = hazard == "gas" ? 90 : app.IsNewGfx ? 92 : 91;
                    if (hazards.Length > 0)
                        hazards += "  ";
                    hazards += app.ResourceManager.GetString($"newburn?{label}") +
                        $": {(int)System.Math.Round(rate * 100)}%";
                }
                string defense = app.IsNewGfx ? txt.Get("newburn?112") : $"Def: {combat.Defense}%";

                if (ShowsWeaponSlot)
                {
                    // right of the weapon slot: damage, then defense and hazard protection
                    textPos.x = basePos.x + WeaponTextX;
                    statsFont.DrawText(Target, textPos, app.IsNewGfx ? txt.Get("newburn?111") : $"Dmg: {damage}",
                        TextAlignment.Left, VerticalTextAlignment.Top);
                    textPos.y += fontSpacing;
                    string protection = string.Join("  ", new[] { combat.Defense > 0 ? defense : "", hazards }
                        .Where(text => text.Length > 0));
                    statsFont.DrawText(Target, textPos, protection, TextAlignment.Left, VerticalTextAlignment.Top);
                    textPos.y += fontSpacing;
                }
                else
                {
                    textPos.x = basePos.x + 73;
                    if (combat.Defense > 0)
                        statsFont.DrawText(Target, textPos, defense, TextAlignment.Left, VerticalTextAlignment.Top);
                    textPos.x = basePos.x + 20;
                    statsFont.DrawText(Target, textPos, app.IsNewGfx || !(combat.Defense > 0)
                        ? txt.Get("newburn?111") : $"Dmg: {damage}",
                        TextAlignment.Left, VerticalTextAlignment.Top);
                    textPos.y += fontSpacing;
                    statsFont.DrawText(Target, textPos, hazards, TextAlignment.Left, VerticalTextAlignment.Top);
                    textPos.y += fontSpacing;
                }
            }

            pageName = "";
            for (int i = 0; i < pageButtons.Length; i++)
            {
                if (pageButtons[i].IsHover && pageIndices[i] >= 0)
                {
                    namePosition = pageButtons[i].Position + new Vector2(6, -9);
                    pageName = pages[pageIndices[i]].Character.Name;
                }
            }

            if (pageName != "")
            {
                Target.Layer += pageButtons.Length + 1;
                nameFont.DrawText(Target, namePosition, pageName, TextAlignment.Center, VerticalTextAlignment.Top);
            }
        }

        void OnLeftClickItem(Framework.States.StateObject State)
        {
            if (LeftClickItemEvent != null)
            {
                LeftClickItemEvent.Execute(State);
            }
        }

        void OnRightClickItem(Framework.States.StateObject State)
        {
            if (RightClickItemEvent != null)
            {
                RightClickItemEvent.Execute(State);
            }
        }

        void OnPage(int index)
        {
            if (index > 0)
            {
                pageIndices[0] = pageIndices[index];
                for (int i = 1; i < pageIndices.Length; i++)
                    pageIndices[i] = (pageIndices[0] >= i) ? (i - 1) : i;

                for (int i = 0; i < pages.Count; i++)
                    pageButtons[i].Image = "munt.raw?" + (5 + pages[pageIndices[i]].PageNumber);

                activePageIndex = pageIndices[0];
                activePage = pages[activePageIndex];
                bagPage = 0;

                OnSelectPage();
            }
        }
    }
}
