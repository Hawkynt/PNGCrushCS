namespace PngOptimizer.Tests;

[TestFixture]
public sealed class PngPaletteReordererTests {
  [Test]
  public void HilbertSort_ProducesValidPermutation() {
    var palette = new List<(byte R, byte G, byte B, byte A)> {
      (255, 0, 0, 255),
      (0, 255, 0, 255),
      (0, 0, 255, 255),
      (128, 128, 128, 255)
    };

    var order = PngPaletteReorderer.HilbertSort(palette);

    Assert.That(order, Has.Length.EqualTo(4));
    var sorted = order.OrderBy(x => x).ToArray();
    Assert.That(sorted, Is.EqualTo(new[] { 0, 1, 2, 3 }));
  }

  [Test]
  public void SpatialLocalitySort_ProducesValidPermutation() {
    var palette = new List<(byte R, byte G, byte B, byte A)> {
      (255, 0, 0, 255),
      (0, 255, 0, 255),
      (0, 0, 255, 255),
      (128, 128, 128, 255)
    };

    // Scanline data where index 2 appears first, then 0, then 1, then 3
    byte[][] scanlines = [[2, 0, 1, 3]];

    var order = PngPaletteReorderer.SpatialLocalitySort(palette, scanlines, 4, 8);

    Assert.That(order, Has.Length.EqualTo(4));
    var sorted = order.OrderBy(x => x).ToArray();
    Assert.That(sorted, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    // First occurrence order: index 2 at pos 0, index 0 at pos 1, index 1 at pos 2, index 3 at pos 3
    Assert.That(order[0], Is.EqualTo(2));
    Assert.That(order[1], Is.EqualTo(0));
  }

  [Test]
  public void DeflateOptimized_PicksSmallestOrdering() {
    var palette = new List<(byte R, byte G, byte B, byte A)> {
      (255, 0, 0, 255),
      (0, 255, 0, 255),
      (0, 0, 255, 255),
      (128, 128, 128, 255)
    };

    byte[][] scanlines = [
      [0, 0, 1, 1, 2, 2, 3, 3],
      [0, 0, 1, 1, 2, 2, 3, 3],
      [3, 3, 2, 2, 1, 1, 0, 0],
      [3, 3, 2, 2, 1, 1, 0, 0]
    ];

    var identityOrder = PngPaletteReorderer.IdentityOrder(4);
    var result = PngPaletteReorderer.DeflateOptimizedSort(palette, scanlines, 8, 8, 1, identityOrder);

    Assert.That(result, Has.Length.EqualTo(4));
    var sorted = result.OrderBy(x => x).ToArray();
    Assert.That(sorted, Is.EqualTo(new[] { 0, 1, 2, 3 }));
  }

  [Test]
  public void IdentityOrder_ReturnsSequentialIndices() {
    var order = PngPaletteReorderer.IdentityOrder(5);
    Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
  }
}
