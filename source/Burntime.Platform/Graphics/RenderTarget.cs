namespace Burntime.Platform.Graphics;

public class RenderTarget
{
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Zoom around the target origin: positions (including <see cref="Offset"/>), sprites and
    /// rectangles are multiplied by it.
    /// </summary>
    public float Zoom { get; set; } = 1;

    bool IsZoomed => Zoom != 1;

    Vector2f Place(Vector2f pos) => (Vector2f)_rc.Position + (pos + Offset) * Zoom;

    float BeginScale()
    {
        float previous = _engine.SpriteScale;
        _engine.SpriteScale = previous * Zoom;
        return previous;
    }
    public float Layer
    {
        get => _engine.Layer;
        set => _engine.Layer = value;
    }

    public int Width => _rc.Width;
    public int Height => _rc.Height;
    public Vector2 ScreenOffset => _rc.Position;
    public Vector2 ScreenSize => _engine.MainTarget.Size;
    public Vector2 Size => _rc.Size;

#warning move set to engine/renderdevice
    public float Elapsed { get; set; }
    public float TotalElapsed { get; set; }

    public RenderTarget(IEngine engine, Rect rc)
    {
        _engine = engine;
        _rc = rc;
    }

    #region DrawSprite methods
    public void DrawSprite(ISprite sprite)
    {
        if (sprite == null)
            return;

        DrawSprite(Vector2.Zero, sprite);
    }

    public void DrawSprite(Vector2 pos, ISprite sprite)
    {
        if (sprite == null)
            return;

        if (IsZoomed)
            DrawSpriteF(pos, sprite, 1);
        else
            _engine.RenderSprite(sprite, pos + _rc.Position + Offset);
    }

    public void DrawSprite(Vector2 pos, ISprite sprite, float alpha,
        bool postFilter = false, bool directToFramebuffer = false)
    {
        if (sprite == null)
            return;

        if (IsZoomed)
            DrawSpriteF(pos, sprite, alpha, postFilter, directToFramebuffer);
        else
            _engine.RenderSprite(sprite, pos + _rc.Position + Offset, alpha, postFilter,
                directToFramebuffer);
    }

    public void DrawSpriteF(Vector2f pos, ISprite sprite, float alpha,
        bool postFilter = false, bool directToFramebuffer = false)
    {
        if (sprite == null)
            return;

        if (!IsZoomed)
        {
            _engine.RenderSpriteF(sprite, pos + _rc.Position + Offset, alpha, postFilter,
                directToFramebuffer);
            return;
        }

        float previous = BeginScale();
        _engine.RenderSpriteF(sprite, Place(pos), alpha, postFilter, directToFramebuffer);
        _engine.SpriteScale = previous;
    }

    public void DrawSprite(Vector2 pos, ISprite sprite, Rect srcRect)
    {
        if (sprite == null)
            return;

        DrawSprite(pos, sprite);
    }

    ISprite? _selectedSprite;
    public void SelectSprite(ISprite sprite)
    {
        _selectedSprite = sprite;
    }

    public void DrawSelectedSprite(Vector2 pos, Rect srcRect, PixelColor color)
    {
        if (_selectedSprite is null) return;

        if (IsZoomed)
        {
            DrawSelectedSpriteF(pos, srcRect, color);
            return;
        }

        _engine.RenderSprite(_selectedSprite, pos + _rc.Position + Offset, srcRect.Position, srcRect.Width, srcRect.Height, color);
    }

    public void DrawSelectedSpriteF(Vector2f pos, Rect srcRect, PixelColor color,
        bool postFilter = false, bool directToFramebuffer = false)
    {
        if (_selectedSprite is null) return;

        if (!IsZoomed)
        {
            _engine.RenderSpriteF(_selectedSprite, pos + _rc.Position + Offset,
                srcRect.Position, srcRect.Width, srcRect.Height, color, postFilter,
                directToFramebuffer);
            return;
        }

        float previous = BeginScale();
        _engine.RenderSpriteF(_selectedSprite, Place(pos),
            srcRect.Position, srcRect.Width, srcRect.Height, color, postFilter,
            directToFramebuffer);
        _engine.SpriteScale = previous;
    }
    #endregion

    public void RenderRect(Vector2 pos, Vector2 size, PixelColor color, bool postFilter = false)
    {
        if (!IsZoomed)
        {
            _engine.RenderRect(pos + _rc.Position + Offset, size, color, postFilter);
            return;
        }

        float previous = BeginScale();
        _engine.RenderRect(new Vector2(Place(pos)), size, color, postFilter);
        _engine.SpriteScale = previous;
    }

    public void RenderLine(Vector2 start, Vector2 end, PixelColor color)
    {
        if (IsZoomed)
            _engine.RenderLine(new Vector2(Place(start)), new Vector2(Place(end)), color);
        else
            _engine.RenderLine(start + _rc.Position + Offset, end + _rc.Position + Offset, color);
    }

    public RenderTarget GetSubBuffer(Rect rc)
    {
        RenderTarget target = new(_engine, new Rect(_rc.Position + rc.Position, rc.Size))
        {
            Elapsed = Elapsed,
            TotalElapsed = TotalElapsed,
            Offset = Offset,
            Zoom = Zoom
        };
        return target;
    }

    public Vector2f SnapToPhysicalPixels(Vector2f pos)
    {
        Vector2f scale = _engine.Resolution.Scale;
        Vector2f absolute = pos + _rc.Position + Offset;

        if (scale.x != 0)
            absolute.x = (float)System.Math.Round(absolute.x * scale.x) / scale.x;
        if (scale.y != 0)
            absolute.y = (float)System.Math.Round(absolute.y * scale.y) / scale.y;

        return absolute - _rc.Position - Offset;
    }

    readonly IEngine _engine;
    readonly Rect _rc;
}
