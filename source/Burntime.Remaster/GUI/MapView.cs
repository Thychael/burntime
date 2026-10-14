using Burntime.Remaster.Logic;
using Burntime.Remaster.Maps;
using Burntime.Data.BurnGfx;
using Burntime.Framework;
using Burntime.Framework.Event;
using Burntime.Framework.GUI;
using Burntime.Platform;
using Burntime.Platform.Graphics;
using System;
using System.Collections.Generic;

namespace Burntime.Remaster.GUI;

public interface IMapEntranceHandler
{
    String GetEntranceTitle(int Number);
    bool OnClickEntrance(int Number, MouseButton Button);
}

public class MapScrollArgs : EventArgs
{
    public MapScrollArgs(Vector2 offset)
    {
        this.offset = offset;
    }

    Vector2 offset;
    public Vector2 Offset
    {
        get { return offset; }
    }
}

public class ObjectArgs : EventArgs
{
    public ObjectArgs(IMapObject obj, Vector2 position, MouseButton button)
    {
        this.obj = obj;
        this.button = button;
        this.position = position;
    }

    IMapObject obj;
    public IMapObject Object
    {
        get { return obj; }
    }

    MouseButton button;
    public MouseButton Button
    {
        get { return button; }
    }

    Vector2 position;
    public Vector2 Position
    {
        get { return position; }
    }
}

public class MapView : Window
{
    protected override bool IsPromptActive(InputMode inputMode) => true;

    public event EventHandler<ObjectArgs> ClickObject;
    public event EventHandler<MapScrollArgs> Scroll;

    Vector2 mousePosition = new Vector2();

    List<Maps.IMapViewOverlay> overlays = new List<Maps.IMapViewOverlay>();
    public List<Maps.IMapViewOverlay> Overlays
    {
        get { return overlays; }
        set { overlays = value; }
    }

    IMapEntranceHandler handler;
    MouseClickEvent mouseClickEvent;
    public MouseClickEvent MouseClickEvent
    {
        get { return mouseClickEvent; }
        set { mouseClickEvent = value; }
    }

    public ParticleEngine Particles { get; } = new ParticleEngine();
    public bool Enabled { get; set; }
    public event ClickHandler ContextMenu;

    public MapView(IMapEntranceHandler Handler, Module App)
        : base(App)
    {
        Enabled = true;
        handler = Handler;
        CaptureAllMouseMove = true;
        CaptureAllMouseClicks = true;

        BurntimeClassic classic = app as BurntimeClassic;
        DebugView = classic.Settings["debug"].GetBool("show_routes") && classic.Settings["debug"].GetBool("enable_cheats");
    }

    MapData map;
    public MapData Map
    {
        get { return map; }
        set { map = value; }
    }

    WayData ways;
    public WayData Ways
    {
        get { return ways; }
        set { ways = value; }
    }

    public Location Location { get; set; }
    public Player Player { get; set; }

    /// <summary>Sprites drawn on top of the map tiles at map positions (e.g. random site icons).</summary>
    public IReadOnlyList<(Vector2 Position, ISprite Sprite)> Decorations { get; set; } = [];

    public bool DebugView = false;

    Vector2f _position = new Vector2f();
    public Vector2 ScrollPosition
    {
        get { return new Vector2(_position); }
        set { _position = value; }
    }

    float scrollSpeed = 70;

    #region Zoom
    static readonly float[] ZoomLevels = [0.5f, 1f, 2f];
    float _zoom = 1;

    /// <summary>Enables mouse wheel zoom (world map only).</summary>
    public bool AllowZoom { get; set; }

    /// <summary>Screen pixels per map pixel. Scroll positions stay in map pixels.</summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = System.Math.Clamp(value, ZoomLevels[0], ZoomLevels[^1]);
            if (ConstrainPosition())
                Scroll?.Invoke(this, new MapScrollArgs(_position));
        }
    }

    /// <summary>Part of the map that fits into the view, in map pixels.</summary>
    Vector2f ViewSize => (Vector2f)Size / _zoom;

    /// <summary>View-relative screen position to view-relative map position (before scrolling).</summary>
    Vector2 Unzoom(Vector2 position) => (Vector2)((Vector2f)position / _zoom);

    public override bool OnMouseWheel(Vector2 position, int delta)
    {
        if (!AllowZoom || !Enabled || map is null || delta == 0)
            return false;

        int level = System.Array.IndexOf(ZoomLevels, _zoom);
        if (level < 0)
            level = System.Array.IndexOf(ZoomLevels, 1f);
        int next = System.Math.Clamp(level + (delta > 0 ? 1 : -1), 0, ZoomLevels.Length - 1);
        if (next == level)
            return true;

        // keep the map point under the cursor in place
        Vector2f anchor = (Vector2f)position / _zoom - _position;
        _zoom = ZoomLevels[next];
        _position = (Vector2f)position / _zoom - anchor;
        _touchPanVelocity = Vector2f.Zero;
        ConstrainPosition();
        Scroll?.Invoke(this, new MapScrollArgs(_position));
        entrance = HitTestEntrance(position);
        mousePosition = Unzoom(position) - ScrollPosition;
        return true;
    }
    #endregion

    Vector2f border = new Vector2f();
    bool mouseEdgeScrollArmed = true;
    int entrance = -1;

    public void RequireMouseEdgeScrollReentry()
    {
        border = Vector2f.Zero;
        mouseEdgeScrollArmed = false;
    }

    public int ActiveEntrance
    {
        get => HoveredObject is EntranceObject entrance ? entrance.Number : -1;
        set => HoveredObject = map != null && value >= 0 && value < map.Entrances.Length && HasLocation(value)
            ? new EntranceObject(map.Entrances[value], value)
            : null;
    }

    // Saves can predate map-defined locations whose entrances already exist on the world map.
    bool HasLocation(int entrance) =>
        !(app.GameState is ClassicGame game && game.MainMapView && entrance >= game.World.Locations.Count);

    public IMapObject? HoveredObject { get; set; }

    public override void OnRender(RenderTarget Target)
    {
        base.OnRender(Target);
        if (map is null) return;

        Vector2 offset = ScrollPosition;
        Target.Zoom = _zoom;
        Vector2 viewSize = (Vector2)ViewSize;

        // pre-fetch all tiles for smooth scrolling
        foreach (var tile in Map.Tiles)
            tile.Image.Touch();

        Rect visible = new Rect(-offset / map.TileSize, viewSize / map.TileSize + 2);
        visible = visible.Intersect(new Rect(0, 0, map.Width, map.Height));

        foreach (Vector2 pos in visible)
        {
            Vector2 trans = pos * map.TileSize + offset;
            Target.DrawSprite(trans, map[pos.x, pos.y].Image);
        }

        Target.Layer++;

        foreach (var (position, sprite) in Decorations)
            Target.DrawSprite(position + offset, sprite);

        if (DebugView)
        {
            foreach (Vector2 pos in new Rect(0, 0, map.Mask.Width, map.Mask.Height))
            {
                if (!map.Mask[pos])
                    Target.RenderRect(offset + pos * map.Mask.Resolution, Vector2.One * map.Mask.Resolution, new PixelColor(50, 255, 0, 0));
            }
            for (int i = 0; i < map.Entrances.Length; i++)
                Target.RenderRect(map.Entrances[i].Area.Position + offset, map.Entrances[i].Area.Size, new PixelColor(50, 0, 0, 255));
        }

        List<int> renderWays = new List<int>();

        int e = entrance;
        if (e != -1)
        {
            if (DebugView && ways != null)
            {
                foreach (int way in ways.Cross[e].Ways)
                    renderWays.Add(way);
            }
        }

        foreach (int way in renderWays)
        {
            if (ways.Ways[way].Images.Length > 0)
            {
                Vector2 pos = ways.Ways[way].Position + offset;

                foreach (var sprite in ways.Ways[way].Images)
                {
                    Target.DrawSprite(pos, sprite);
                    pos.x += 32;
                }
            }
        }

        Target.Layer++;

        var old = Target.Layer;
        Target.Layer += 30;

        foreach (Maps.IMapViewOverlay overlay in overlays)
        {
            if (!overlay.IsVisible)
                continue;
            Target.Layer++;
            // Text overlays are rendered unzoomed and only zoom their anchor positions.
            overlay.Zoom = _zoom;
            Target.Zoom = overlay.ZoomSprites ? _zoom : 1;
            overlay.RenderOverlay(Target, offset, viewSize);
            Target.Zoom = _zoom;
        }

        Target.Layer++;
        Target.Offset += ScrollPosition;
        Particles.Render(Target);
        Target.Offset -= ScrollPosition;

        Target.Layer = old;
    }

    public override bool OnMouseMove(Vector2 position)
    {
        if (!Enabled)
            return false;

        const int Margin = 4;
        const int BigMargin = 10;

        border.x = 0;
        border.y = 0;

        border.x += (position.x < Margin) ? 1 : 0;
        border.x -= (position.x > Size.x - Margin) ? 1 : 0;
        border.x += (position.x < BigMargin) ? 1 : 0;
        border.x -= (position.x > Size.x - BigMargin) ? 1 : 0;
        border.y += (position.y < Margin) ? 1 : 0;
        border.y -= (position.y > Size.y - Margin) ? 1 : 0;
        border.y += (position.y < BigMargin) ? 1 : 0;
        border.y -= (position.y > Size.y - BigMargin) ? 1 : 0;

        if (!mouseEdgeScrollArmed)
        {
            mouseEdgeScrollArmed = position.x >= BigMargin &&
                position.x <= Size.x - BigMargin &&
                position.y >= BigMargin && position.y <= Size.y - BigMargin;
            if (!mouseEdgeScrollArmed)
                border = Vector2f.Zero;
        }

        entrance = HitTestEntrance(position);

        mousePosition = Unzoom(position) - ScrollPosition;

        if (_rightClickMove.HasValue)
        {
            _position += (Vector2f)(position - _rightClickMove.Value) / _zoom;
            ConstrainPosition();

            _moveTotal += (position - _rightClickMove.Value).Length;
            _rightClickMove = position;
        }

        return true;
    }

    public override void OnMouseEnter()
    {
        if (_rightClickMove.HasValue && !app.Engine.DeviceManager.Mouse.IsRightDown)
            _rightClickMove = null;

        base.OnMouseEnter();
    }

    public override void OnMouseLeave()
    {
        border = Vector2f.Zero;
        entrance = -1;
        base.OnMouseLeave();
    }

    public override void OnActivate()
    {
        _rightClickMove = null;
        _touchPanVelocity = Vector2f.Zero;
        _suppressTouchAction = false;

        base.OnActivate();
    }

    public override bool OnMouseClick(Vector2 position, MouseButton button)
    {
        if (!Enabled)
            return false;

        if (button == MouseButton.Right)
        {
            bool wasDrag = _moveTotal >= 10;
            if (!wasDrag)
                ContextMenu?.Invoke(position, button);
            _rightClickMove = null;
            _moveTotal = 0;
            if (wasDrag)
                return true;
        }

        // end all capture here
        if (!Boundings.PointInside(position + Position))
            return false;

        entrance = HitTestEntrance(position);

        if (entrance != -1 && handler != null)
            if (handler.OnClickEntrance(entrance, button))
                return true;

        IMapObject mostTopObj = null;
        foreach (Maps.IMapViewOverlay overlay in overlays)
        {
            if (!overlay.IsVisible)
                continue;
            IMapObject obj = overlay.GetObjectAt(Unzoom(position) - ScrollPosition);
            if (obj != null)
                mostTopObj = obj;
        }

        if (mostTopObj != null)
        {
            if (ClickObject != null)
                ClickObject.Invoke(this, new ObjectArgs(mostTopObj, position, button));
            return true;
        }

        if (mouseClickEvent != null && button == MouseButton.Left)
        {
            mouseClickEvent.Execute(Unzoom(position) - ScrollPosition, button);
            return true;
        }

        return false;
    }

    public Action<int>? TouchLocationSecondary { get; set; }

    public int HitTestEntrance(Vector2 position)
    {
        for (int i = overlays.Count - 1; i >= 0; i--)
        {
            Maps.IMapViewOverlay overlay = overlays[i];
            if (!overlay.IsVisible)
                continue;

            if (overlay is Maps.IMapViewEntranceOverlay entranceOverlay)
            {
                int entrance = entranceOverlay.HitTestEntrance(Unzoom(position),
                    ScrollPosition, (Vector2)ViewSize);
                if (entrance >= 0 && HasLocation(entrance))
                    return entrance;
            }
        }

        if (map != null)
        {
            for (int i = map.Entrances.Length - 1; i >= 0; i--)
                if (map.Entrances[i].Area.PointInside(Unzoom(position) - ScrollPosition) && HasLocation(i))
                    return i;
        }

        return -1;
    }

    public override bool OnTouchLongPress(Vector2 position)
    {
        if (_suppressTouchAction)
        {
            _suppressTouchAction = false;
            return true;
        }
        if (!Enabled || TouchLocationSecondary == null) return false;
        int entranceNumber = HitTestEntrance(position);
        if (entranceNumber >= 0)
        {
            TouchLocationSecondary(entranceNumber);
            return true;
        }
        return false;
    }

    float _moveTotal;
    Vector2f _touchPanVelocity;
    bool _suppressTouchAction;
    const float TouchPanMaximumSpeed = 500;
    const float TouchPanStopSpeed = 4;
    const float TouchPanHalfLife = 0.15f;

    public override void OnTouchDrag(Vector2 delta)
    {
        if (!Enabled) return;
        _suppressTouchAction = false;
        _touchPanVelocity = Vector2f.Zero;
        _position += (Vector2f)delta / _zoom;
        ConstrainPosition();
        Scroll?.Invoke(this, new MapScrollArgs(_position));
    }

    public override void OnTouchDragEnd(Vector2f velocity)
    {
        if (!Enabled) return;
        if (velocity.Length > TouchPanMaximumSpeed)
        {
            velocity.Normalize();
            velocity *= TouchPanMaximumSpeed;
        }
        _touchPanVelocity = velocity;
    }

    public override void OnTouchPress(Vector2 position)
    {
        _suppressTouchAction = _touchPanVelocity != Vector2f.Zero;
        _touchPanVelocity = Vector2f.Zero;
    }

    public override bool OnTouchTap(Vector2 position)
    {
        if (!_suppressTouchAction) return false;
        _suppressTouchAction = false;
        return true;
    }

    Vector2? _rightClickMove = null;
    public override bool OnMouseDown(Vector2 position, MouseButton button)
    {
        if (button == MouseButton.Right)
        {
            _moveTotal = 0;
            _rightClickMove = position;
        }

        return true;
    }

    public override void OnUpdate(float Elapsed)
    {
        bool mouseInputVisible = app.MouseInputVisible;
        if (!mouseInputVisible)
        {
            border = Vector2f.Zero;
            entrance = -1;
        }

        bool positionChanged = false;
        bool cameraPanInputDown = false;
        foreach (InputAction action in app.InputManager.ActionsDown)
        {
            if (action is InputAction.PanCameraUp or InputAction.PanCameraDown or
                InputAction.PanCameraLeft or InputAction.PanCameraRight)
            {
                cameraPanInputDown = true;
                break;
            }
        }

        if (cameraPanInputDown)
            _touchPanVelocity = Vector2f.Zero;

        if (_touchPanVelocity != Vector2f.Zero)
        {
            Vector2f proposedPosition = _position + _touchPanVelocity * Elapsed;
            _position = proposedPosition;
            ConstrainPosition();
            if (_position.x != proposedPosition.x)
                _touchPanVelocity.x = 0;
            if (_position.y != proposedPosition.y)
                _touchPanVelocity.y = 0;

            float decay = (float)System.Math.Pow(0.5, Elapsed / TouchPanHalfLife);
            _touchPanVelocity *= decay;
            if (_touchPanVelocity.Length < TouchPanStopSpeed)
                _touchPanVelocity = Vector2f.Zero;
            positionChanged = true;
        }

        if (mouseInputVisible && !cameraPanInputDown &&
            !_rightClickMove.HasValue && border != Vector2f.Zero)
        {
            _position += border * Elapsed * scrollSpeed / _zoom;
            positionChanged = true;
        }

        positionChanged |= ConstrainPosition();
        if (positionChanged)
            Scroll?.Invoke(this, new MapScrollArgs(_position));

        if (handler != null)
        {
            ClassicGame game = app.GameState as ClassicGame;
            if (mouseInputVisible)
                HoveredObject = null;
            game.World.ActiveLocationObj.Hover = null;
            game.World.ActiveLocationObj.HoverCharacter = null;

            foreach (Maps.IMapViewOverlay overlay in overlays)
            {
                if (!overlay.IsVisible)
                    continue;

                if (mouseInputVisible)
                {
                    overlay.MouseMoveOverlay(mousePosition);
                    IMapObject hoveredObject = overlay.GetObjectAt(mousePosition);
                    if (hoveredObject != null)
                        HoveredObject = hoveredObject;
                }
                overlay.UpdateOverlay(game, Elapsed);
            }

            if (mouseInputVisible && entrance != -1)
                HoveredObject = new EntranceObject(map.Entrances[entrance], entrance);

            int activeEntrance = ActiveEntrance;
            if (activeEntrance != -1)
            {
                PixelColor entranceColor = ClassicColors.LightGray;
                if (game.MainMapView)
                {
                    Burntime.Data.BurnGfx.MapEntrance e = game.World.Map.Entrances[activeEntrance];
                    game.World.ActiveLocationObj.Hover = new MapViewHoverInfo(app.ResourceManager.GetString(e.TitleId), e.Area.Center, entranceColor)
                    {
                        WorldLocation = game.World.Locations[activeEntrance]
                    };
                }
                else if (activeEntrance < game.World.ActiveLocationObj.Rooms.Count)
                {
                    Location location = game.World.ActiveLocationObj;
                    game.World.ActiveLocationObj.Hover = location.AreEntrancesBlockedFor(game.World.ActivePlayerObj)
                        ? new MapViewHoverInfo(app.ResourceManager.GetString("newburn?103"), location.Map.Entrances[activeEntrance].Area.Center, entranceColor, location.Rooms[activeEntrance])
                        : new MapViewHoverInfo(location.Rooms[activeEntrance], app.ResourceManager, entranceColor);
                }
            }
        }

        Particles.Update(Elapsed);
    }

    public void CenterTo(Vector2 centerTo)
    {
        _touchPanVelocity = Vector2f.Zero;
        _position = -(Vector2f)centerTo + ViewSize / 2;
        ConstrainPosition();
        Scroll?.Invoke(this, new MapScrollArgs(_position));
    }

    public void SetViewport(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
        if (ConstrainPosition())
            Scroll?.Invoke(this, new MapScrollArgs(_position));
    }

    public bool FollowWithinMiddleThird(Vector2 mapPosition, float elapsed)
    {
        if (map is null)
            return true;

        Vector2f oldPosition = _position;
        Vector2f screenPosition = (Vector2f)mapPosition + _position;
        Vector2f minimum = ViewSize / 3;
        Vector2f maximum = ViewSize * (2f / 3f);
        Vector2f desiredPosition = _position;

        if (screenPosition.x < minimum.x)
            desiredPosition.x += minimum.x - screenPosition.x;
        else if (screenPosition.x > maximum.x)
            desiredPosition.x -= screenPosition.x - maximum.x;

        if (screenPosition.y < minimum.y)
            desiredPosition.y += minimum.y - screenPosition.y;
        else if (screenPosition.y > maximum.y)
            desiredPosition.y -= screenPosition.y - maximum.y;

        ConstrainPosition(ref desiredPosition);

        Vector2f correction = desiredPosition - _position;
        float maximumDistance = scrollSpeed * 2 * elapsed;
        bool reachedPosition = correction.Length <= maximumDistance;
        if (correction.Length > maximumDistance)
        {
            correction.Normalize();
            correction *= maximumDistance;
        }

        _position += correction;

        ConstrainPosition();
        if (_position != oldPosition)
            Scroll?.Invoke(this, new MapScrollArgs(_position));

        return reachedPosition;
    }

    public void Pan(Vector2 direction, float elapsed)
    {
        if (map is null)
            return;

        _touchPanVelocity = Vector2f.Zero;
        _position -= (Vector2f)direction * elapsed * scrollSpeed / _zoom;
        ConstrainPosition();
        Scroll?.Invoke(this, new MapScrollArgs(_position));
    }

    bool ConstrainPosition()
    {
        if (map is null)
            return false;

        Vector2f oldPosition = _position;
        ConstrainPosition(ref _position);
        return _position != oldPosition;
    }

    void ConstrainPosition(ref Vector2f position)
    {
        Vector2 mapSize = map.TileSize * map.Size;
        Vector2f view = ViewSize;

        if (mapSize.x <= view.x)
            position.x = (view.x - mapSize.x) / 2;
        else
            position.x = System.Math.Clamp(position.x, view.x - mapSize.x, 0);

        if (mapSize.y <= view.y)
            position.y = (view.y - mapSize.y) / 2;
        else
            position.y = System.Math.Clamp(position.y, view.y - mapSize.y, 0);
    }
}
