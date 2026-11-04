// Nestation New City CC BY-NC 3.0
using System.Numerics;
using Content.Shared.Explosion.Components;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Explosion;

[UsedImplicitly]
public sealed partial class ExplosionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> FireShader = "ExplosionFire";

    private const int FieldPadding = 4;
    private const int MaxFieldSide = 2048;
    private const float BirthRange = 12f;
    private const float PowerReference = 40f;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClyde _clyde = default!;
    private readonly SharedTransformSystem _transformSystem;
    private SharedAppearanceSystem _appearance;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public ExplosionOverlay(SharedAppearanceSystem appearanceSystem)
    {
        IoCManager.InjectDependencies(this);
        _transformSystem = _entMan.System<SharedTransformSystem>();
        _appearance = appearanceSystem;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var drawHandle = args.WorldHandle;
        var xforms = _entMan.GetEntityQuery<TransformComponent>();
        var query = _entMan.EntityQueryEnumerator<ExplosionVisualsComponent, ExplosionVisualsTexturesComponent>();

        while (query.MoveNext(out var uid, out var visuals, out var textures))
        {
            if (visuals.Epicenter.MapId != args.MapId)
                continue;

            if (!_appearance.TryGetData(uid, ExplosionAppearanceData.Progress, out int index))
                continue;

            if (visuals.Intensity.Count == 0)
                continue;

            index = Math.Min(index, visuals.Intensity.Count - 1);
            DrawExplosion(drawHandle, args.WorldBounds, uid, visuals, index, xforms, textures);
        }

        drawHandle.UseShader(null);
        drawHandle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawExplosion(
        DrawingHandleWorld drawHandle,
        Box2Rotated worldBounds,
        EntityUid uid,
        ExplosionVisualsComponent visuals,
        int index,
        EntityQuery<TransformComponent> xforms,
        ExplosionVisualsTexturesComponent textures)
    {
        if (!textures.Started)
            Start(uid, visuals, textures);

        var now = (float) (_timing.RealTime - textures.StartTime).TotalSeconds;

        foreach (var (gridId, tiles) in visuals.Tiles)
        {
            if (!_entMan.TryGetComponent(gridId, out MapGridComponent? grid))
                continue;

            var xform = xforms.GetComponent(gridId);
            var (_, _, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(xform, xforms);

            if (!textures.Fields.TryGetValue(gridId, out var field))
            {
                var epicenter = Vector2.Transform(visuals.Epicenter.Position, invWorldMatrix);
                field = BuildField(tiles, grid.TileSize, epicenter, textures);
                textures.Fields[gridId] = field;
            }

            if (field == null)
                continue;

            var gridBounds = invWorldMatrix.TransformBox(worldBounds).Enlarged(grid.TileSize * 2);
            DrawField(drawHandle, field, worldMatrix, grid.TileSize, gridBounds, tiles, visuals, index, now);
        }

        if (visuals.SpaceTiles == null)
            return;

        if (!textures.SpaceFieldBuilt)
        {
            textures.SpaceFieldBuilt = true;
            Matrix3x2.Invert(visuals.SpaceMatrix, out var invMatrix);
            var epicenter = Vector2.Transform(visuals.Epicenter.Position, invMatrix);
            textures.SpaceField = BuildField(visuals.SpaceTiles, visuals.SpaceTileSize, epicenter, textures);
        }

        if (textures.SpaceField == null)
            return;

        Matrix3x2.Invert(visuals.SpaceMatrix, out var invSpace);
        var spaceBounds = invSpace.TransformBox(worldBounds).Enlarged(2);
        DrawField(drawHandle, textures.SpaceField, visuals.SpaceMatrix, visuals.SpaceTileSize, spaceBounds, visuals.SpaceTiles, visuals, index, now);
    }

    private void Start(EntityUid uid, ExplosionVisualsComponent visuals, ExplosionVisualsTexturesComponent textures)
    {
        textures.Started = true;
        textures.StartTime = _timing.RealTime;
        textures.Seed = (uid.Id % 997) / 997f;

        var max = 0f;
        foreach (var intensity in visuals.Intensity)
        {
            max = Math.Max(max, intensity);
        }

        textures.MaxIntensity = Math.Max(max, 0.001f);
        textures.Power = Math.Clamp(MathF.Sqrt(max / PowerReference), 0.4f, 1.6f);
    }

    private ExplosionField? BuildField(
        Dictionary<int, List<Vector2i>> tileSets,
        ushort tileSize,
        Vector2 epicenterLocal,
        ExplosionVisualsTexturesComponent textures)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var list in tileSets.Values)
        {
            foreach (var tile in list)
            {
                minX = Math.Min(minX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxX = Math.Max(maxX, tile.X);
                maxY = Math.Max(maxY, tile.Y);
            }
        }

        if (minX > maxX)
            return null;

        var origin = new Vector2i(minX - FieldPadding, minY - FieldPadding);
        var size = new Vector2i(maxX - minX + 1 + 2 * FieldPadding, maxY - minY + 1 + 2 * FieldPadding);

        if (size.X > MaxFieldSide || size.Y > MaxFieldSide)
            return null;

        var epicenterTiles = epicenterLocal / tileSize;

        var radius = 1f;
        foreach (var list in tileSets.Values)
        {
            foreach (var tile in list)
            {
                radius = Math.Max(radius, Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), epicenterTiles));
            }
        }

        var expandTime = Math.Clamp(0.3f + 0.05f * radius, 0.35f, 1.6f);
        var speed = radius / expandTime;

        var shader = _proto.Index(FireShader).InstanceUnique();
        var field = new ExplosionField(_clyde, shader, origin, size, epicenterTiles, speed);

        var tint = textures.FireColor;
        shader.SetParameter("uHeat", field.Heat);
        shader.SetParameter("uBirth", field.Birth);
        shader.SetParameter("uSize", new Vector2(size.X, size.Y));
        shader.SetParameter("uEpi", field.Epicenter);
        shader.SetParameter("uSeed", textures.Seed);
        shader.SetParameter("uPower", textures.Power);
        shader.SetParameter("uSpeed", speed);
        shader.SetParameter("uTint", tint == null ? Vector3.One : new Vector3(tint.Value.R, tint.Value.G, tint.Value.B));
        shader.SetParameter("uTintMix", tint == null ? 0f : 1f);
        shader.SetParameter("uNow", 0f);

        return field;
    }

    private void DrawField(
        DrawingHandleWorld drawHandle,
        ExplosionField field,
        Matrix3x2 matrix,
        ushort tileSize,
        Box2 bounds,
        Dictionary<int, List<Vector2i>> tileSets,
        ExplosionVisualsComponent visuals,
        int index,
        float now)
    {
        Reveal(field, tileSets, visuals, index, now);

        var rect = new Box2(field.Origin * tileSize, (field.Origin + field.Size) * tileSize);
        if (!rect.Intersects(bounds))
            return;

        field.Shader.SetParameter("uNow", now);
        drawHandle.SetTransform(matrix);
        drawHandle.UseShader(field.Shader);
        drawHandle.DrawTextureRect(field.Presence, rect);
    }

    private void Reveal(
        ExplosionField field,
        Dictionary<int, List<Vector2i>> tileSets,
        ExplosionVisualsComponent visuals,
        int index,
        float now)
    {
        if (field.Revealed >= index)
            return;

        var maxIntensity = 0f;
        foreach (var intensity in visuals.Intensity)
        {
            maxIntensity = Math.Max(maxIntensity, intensity);
        }

        maxIntensity = Math.Max(maxIntensity, 0.001f);

        for (var j = field.Revealed + 1; j <= index; j++)
        {
            if (!tileSets.TryGetValue(j, out var tiles))
                continue;

            var ratio = Math.Clamp(visuals.Intensity[j] / maxIntensity, 0f, 1f);
            var heat = 0.3f + 0.7f * MathF.Pow(ratio, 0.6f);
            var heatByte = (byte) (heat * 255f);

            foreach (var tile in tiles)
            {
                var distance = Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), field.EpicenterTiles);
                var birth = Math.Max(distance / field.Speed, now);
                var birthByte = (byte) (Math.Clamp(birth / BirthRange, 0f, 1f) * 255f);

                var i = field.IndexOf(tile);
                field.PresenceData[i] = new Rgba32(255, 255, 255, 255);
                field.HeatData[i] = new Rgba32(0, 0, 0, heatByte);
                field.BirthData[i] = new Rgba32(0, 0, 0, birthByte);
            }
        }

        field.Revealed = index;
        field.Upload();
    }
}
