using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Geometry of the map refinement pass: grid bucketing, margin-grown crop rectangles,
/// and the crop↔full-image coordinate mapping.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class MapRefinementTests
{
    private static MapPlace Place(string name, decimal x, decimal y) =>
        new(name, null, x, y, null, null);

    [Test]
    public void PlanTiles_BucketsPlacesByGridCell()
    {
        var places = new[]
        {
            Place("top-left", 0.1m, 0.1m),      // cell 0
            Place("also-top-left", 0.2m, 0.2m), // cell 0
            Place("center", 0.5m, 0.5m),        // cell 4
            Place("bottom-right", 0.9m, 0.9m)   // cell 8
        };

        var tiles = MapRefinement.PlanTiles(places);

        Assert.That(tiles, Has.Count.EqualTo(3));
        Assert.That(tiles.Select(t => t.Index), Is.EqualTo(new[] { 0, 4, 8 }));
        Assert.That(tiles[0].PlaceIndices, Is.EqualTo(new[] { 0, 1 }));
        Assert.That(tiles[1].PlaceIndices, Is.EqualTo(new[] { 2 }));
        Assert.That(tiles[2].PlaceIndices, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void PlanTiles_EdgePosition_LandsInLastCell_NotOutOfRange()
    {
        var tiles = MapRefinement.PlanTiles([Place("corner", 1m, 1m)]);

        Assert.That(tiles, Has.Count.EqualTo(1));
        Assert.That(tiles[0].Index, Is.EqualTo(MapRefinement.Grid * MapRefinement.Grid - 1));
    }

    [Test]
    public void PlanTiles_TileRects_GrowByMarginAndClampToImage()
    {
        var tiles = MapRefinement.PlanTiles([Place("top-left", 0.1m, 0.1m), Place("center", 0.5m, 0.5m)]);

        var corner = tiles.Single(t => t.Index == 0);
        Assert.That(corner.X, Is.EqualTo(0m));
        Assert.That(corner.Y, Is.EqualTo(0m));
        Assert.That(corner.Width, Is.EqualTo(1m / 3 + MapRefinement.Margin).Within(0.0001m));

        var center = tiles.Single(t => t.Index == 4);
        Assert.That(center.X, Is.EqualTo(1m / 3 - MapRefinement.Margin).Within(0.0001m));
        Assert.That(center.Width, Is.EqualTo(1m / 3 + 2 * MapRefinement.Margin).Within(0.0001m));
    }

    [Test]
    public void CropAndFullImageMapping_RoundTrips()
    {
        var tiles = MapRefinement.PlanTiles([Place("center", 0.5m, 0.6m)]);
        var tile = tiles.Single();

        var (cropX, cropY) = MapRefinement.ToCrop(tile, 0.5m, 0.6m);
        var (fullX, fullY) = MapRefinement.ToFullImage(tile, cropX, cropY);

        Assert.That(fullX, Is.EqualTo(0.5m).Within(0.0001m));
        Assert.That(fullY, Is.EqualTo(0.6m).Within(0.0001m));
    }

    [Test]
    public void ToFullImage_ClampsInsideImage()
    {
        var tile = new MapRefinement.Tile(8, 0.5666m, 0.5666m, 0.4334m, 0.4334m, [0]);

        var (x, y) = MapRefinement.ToFullImage(tile, 1m, 1m);

        Assert.That(x, Is.LessThanOrEqualTo(1m));
        Assert.That(y, Is.LessThanOrEqualTo(1m));
    }

    [Test]
    public void CropTiles_ProducesOnePngPerTile_WithExpectedSize()
    {
        using var image = new Image<Rgba32>(300, 150);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);

        var tiles = MapRefinement.PlanTiles([Place("top-left", 0.1m, 0.1m), Place("bottom-right", 0.95m, 0.95m)]);
        var crops = MapRefinement.CropTiles(buffer.ToArray(), tiles);

        Assert.That(crops.Keys, Is.EquivalentTo(new[] { 0, 8 }));
        using var crop = Image.Load(crops[0]);
        // Tile 0 spans 0..(1/3 + margin) of a 300×150 image.
        Assert.That(crop.Width, Is.EqualTo(130));
        Assert.That(crop.Height, Is.EqualTo(65));
    }

    [Test]
    public void CropTiles_UndecodableImage_Throws()
    {
        var tiles = MapRefinement.PlanTiles([Place("anywhere", 0.5m, 0.5m)]);

        Assert.That(() => MapRefinement.CropTiles([1, 2, 3], tiles), Throws.Exception);
    }
}
