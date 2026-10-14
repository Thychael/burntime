using System;
using System.Collections.Generic;
using System.Text;

using Burntime.Platform;
using Burntime.Platform.Graphics;
using Burntime.Framework;
using Burntime.Remaster.Logic;

namespace Burntime.Remaster.GUI
{
    public class ClassicMapView : MapView
    {
        public ClassicMapView(IMapEntranceHandler Handler, Module App)
            : base(Handler, App)
        {
        }

        public override void OnRender(RenderTarget Target)
        {
            base.OnRender(Target);
            Target.Layer += 2;

            if (Map == null)
                return;

            List<int> renderWays = new List<int>();
            Vector2 offset = (Vector2)ScrollPosition;
            ClassicGame game = (ClassicGame)app.GameState;

            for (int i = 0; i < 4; i++)
            {
                Player p = game.World.Players[i];
                if (p.IsDead)
                    continue;

                if (p.IsTraveling && p.Destination != null)
                {
                    // Look the way up by its end points: map-defined locations have no
                    // GAM.DAT way ids, so Location.Ways can be shorter than Neighbors.
                    int from = p.Location.Id;
                    if (from >= 0 && from < Ways.Cross.Length && Ways.Cross[from].Ways != null)
                    {
                        foreach (int way in Ways.Cross[from].Ways)
                        {
                            if (Ways.Ways[way].Start == p.Destination.Id || Ways.Ways[way].End == p.Destination.Id)
                            {
                                renderWays.Add(way);
                                break;
                            }
                        }
                    }
                }
            }

            int active = game.World.ActivePlayer;
            if (active != -1)
            {
                foreach (int way in Ways.Cross[game.World.ActivePlayerObj.Location].Ways)
                {
                    Player p = game.World.ActivePlayerObj;

                    // get start point and end point
                    Location start = p.Location;
                    Location end = game.World.Locations[Ways.Ways[way].End];
                    if (end == start)
                        end = game.World.Locations[Ways.Ways[way].Start];
                    
                    // check travel allowance
                    if (p.CanTravel(start, end))
                        renderWays.Add(way);
                }
            }

            foreach (int way in renderWays)
            {
                if (Ways.Ways[way].Images.Length > 0)
                {
                    Vector2 pos = Ways.Ways[way].Position + offset;

                    foreach (ISprite sprite in Ways.Ways[way].Images)
                    {
                        Target.DrawSprite(pos, sprite);
                        pos.x += 32;
                    }
                }
            }

        }
    }
}
