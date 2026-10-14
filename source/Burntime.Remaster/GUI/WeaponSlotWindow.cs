using System;
using Burntime.Framework;
using Burntime.Framework.GUI;
using Burntime.Platform;
using Burntime.Platform.Graphics;
using Burntime.Remaster.Logic;

namespace Burntime.Remaster.GUI
{
    /// <summary>
    /// Weapon slot of the inventory panel (Extended rules). Frame and item are drawn smaller than
    /// the regular item slots so the slot fits into the gap below the portrait.
    /// </summary>
    class WeaponSlotWindow : Window
    {
        public const int SlotSize = 26;
        const float ItemScale = 0.75f;

        readonly GuiImage frame = "gfx/ui/weapon_slot.png";
        GuiImage? image;
        string? imageSprite;
        Item? item;

        public event Action<Item>? LeftClick;
        public event Action<Item>? RightClick;

        public WeaponSlotWindow(Module app)
            : base(app)
        {
            Size = new Vector2(SlotSize, SlotSize);
        }

        public Item? Item
        {
            get => item;
            set
            {
                item = value;
                string? sprite = value?.Sprite;
                if (sprite != imageSprite)
                {
                    image = string.IsNullOrEmpty(sprite) ? null : (GuiImage)sprite;
                    imageSprite = sprite;
                }
            }
        }

        public override void OnRender(RenderTarget target)
        {
            target.DrawSprite(Vector2.Zero, frame);
            if (image == null)
                return;

            // 32x32 item at 24x24, centered in the 26x26 frame
            target.Layer++;
            target.Zoom = ItemScale;
            target.DrawSpriteF(new Vector2f(1, 1) / ItemScale, image, 1);
            target.Zoom = 1;
        }

        public override bool OnMouseClick(Vector2 position, MouseButton button)
        {
            if (item == null)
                return false;
            if (button == MouseButton.Left)
                LeftClick?.Invoke(item);
            else if (button == MouseButton.Right)
                RightClick?.Invoke(item);
            return true;
        }
    }
}
