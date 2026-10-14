using System;
using System.Collections.Generic;
using System.Text;

using Burntime.Platform;
using Burntime.Platform.Graphics;
using Burntime.Framework.States;

namespace Burntime.Remaster.Maps
{
    public interface IMapViewOverlay
    {
        void MouseMoveOverlay(Vector2 Position);
        void UpdateOverlay(WorldState world, float elapsed);
        void RenderOverlay(RenderTarget Target, Vector2 Offset, Vector2 Size);

        bool IsVisible { get; set; }
        IMapObject GetObjectAt(Vector2 position);

        /// <summary>
        /// False for overlays that draw text: they are rendered unzoomed and place their text at
        /// zoomed positions themselves (see <see cref="Zoom"/>), so text keeps its size.
        /// </summary>
        bool ZoomSprites => true;

        /// <summary>Map zoom for overlays with <see cref="ZoomSprites"/> false.</summary>
        float Zoom { get => 1; set { } }
    }

    public interface IMapViewEntranceOverlay
    {
        int HitTestEntrance(Vector2 position, Vector2 offset, Vector2 size);
    }
}
