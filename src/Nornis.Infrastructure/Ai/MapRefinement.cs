using Nornis.Application.Ai;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Geometry for the map extraction refinement pass. The first pass reads the whole map and
/// its positions land within a few percent of the truth; the refinement pass re-asks the
/// model per cropped tile, where each place is large in frame and the answer is far more
/// precise. Tiles are a fixed grid grown by a margin so a place whose first-pass position
/// sits near a tile edge is still fully visible in its tile's crop.
/// </summary>
internal static class MapRefinement
{
    internal const int Grid = 3;
    internal const decimal Margin = 0.10m;

    /// <summary>A crop rectangle in normalized full-image coordinates, plus the indices
    /// (into the first-pass place list) of the places whose refinement it carries.</summary>
    internal sealed record Tile(int Index, decimal X, decimal Y, decimal Width, decimal Height, IReadOnlyList<int> PlaceIndices);

    /// <summary>Buckets places into grid cells by their first-pass position; only occupied
    /// cells become tiles. Positions outside 0..1 were already filtered by the caller.</summary>
    internal static IReadOnlyList<Tile> PlanTiles(IReadOnlyList<MapPlace> places)
    {
        var buckets = new Dictionary<int, List<int>>();
        for (var i = 0; i < places.Count; i++)
        {
            var col = Math.Min(Grid - 1, (int)(places[i].X * Grid));
            var row = Math.Min(Grid - 1, (int)(places[i].Y * Grid));
            var cell = row * Grid + col;
            if (!buckets.TryGetValue(cell, out var indices))
            {
                buckets[cell] = indices = [];
            }
            indices.Add(i);
        }

        var tiles = new List<Tile>(buckets.Count);
        foreach (var (cell, indices) in buckets.OrderBy(kv => kv.Key))
        {
            var col = cell % Grid;
            var row = cell / Grid;
            var x0 = Math.Max(0m, (decimal)col / Grid - Margin);
            var y0 = Math.Max(0m, (decimal)row / Grid - Margin);
            var x1 = Math.Min(1m, (decimal)(col + 1) / Grid + Margin);
            var y1 = Math.Min(1m, (decimal)(row + 1) / Grid + Margin);
            tiles.Add(new Tile(cell, x0, y0, x1 - x0, y1 - y0, indices));
        }
        return tiles;
    }

    /// <summary>A position within a tile's crop mapped back to full-image coordinates.</summary>
    internal static (decimal X, decimal Y) ToFullImage(Tile tile, decimal cropX, decimal cropY) =>
        (Math.Clamp(tile.X + cropX * tile.Width, 0m, 1m),
         Math.Clamp(tile.Y + cropY * tile.Height, 0m, 1m));

    /// <summary>A full-image position expressed within a tile's crop (the hint given to the model).</summary>
    internal static (decimal X, decimal Y) ToCrop(Tile tile, decimal fullX, decimal fullY) =>
        (Math.Clamp((fullX - tile.X) / tile.Width, 0m, 1m),
         Math.Clamp((fullY - tile.Y) / tile.Height, 0m, 1m));

    /// <summary>Decodes the map once and encodes one PNG crop per tile.</summary>
    internal static Dictionary<int, byte[]> CropTiles(byte[] imageBytes, IReadOnlyList<Tile> tiles)
    {
        using var image = Image.Load(imageBytes);
        var crops = new Dictionary<int, byte[]>(tiles.Count);
        foreach (var tile in tiles)
        {
            var x = Math.Clamp((int)(tile.X * image.Width), 0, image.Width - 1);
            var y = Math.Clamp((int)(tile.Y * image.Height), 0, image.Height - 1);
            var width = Math.Clamp((int)Math.Ceiling(tile.Width * image.Width), 1, image.Width - x);
            var height = Math.Clamp((int)Math.Ceiling(tile.Height * image.Height), 1, image.Height - y);

            using var crop = image.Clone(c => c.Crop(new Rectangle(x, y, width, height)));
            using var buffer = new MemoryStream();
            crop.SaveAsPng(buffer);
            crops[tile.Index] = buffer.ToArray();
        }
        return crops;
    }
}
