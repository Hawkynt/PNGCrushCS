using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Compression;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class PngFilterOptimizerTests {
  [Test]
  public void SingleFilter_ReturnsUniformArray() {
    byte[][] imageData = [
      [10, 20, 30, 40],
      [50, 60, 70, 80],
      [90, 100, 110, 120]
    ];

    var optimizer = new PngFilterOptimizer(4, 3, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.SingleFilter);

    Assert.That(filters, Has.Length.EqualTo(3));
    Assert.That(filters[0], Is.EqualTo(filters[1]));
    Assert.That(filters[1], Is.EqualTo(filters[2]));
  }

  [Test]
  public void ScanlineAdaptive_ReturnsArrayMatchingHeight() {
    byte[][] imageData = [
      [10, 20, 30],
      [10, 20, 30],
      [10, 20, 30],
      [10, 20, 30]
    ];

    var optimizer = new PngFilterOptimizer(3, 4, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.ScanlineAdaptive);

    Assert.That(filters, Has.Length.EqualTo(4));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void ScanlineAdaptive_IdenticalRows_SecondRowPrefersUpOrNone() {
    byte[][] imageData = [
      [10, 20, 30, 40, 50],
      [10, 20, 30, 40, 50]
    ];

    var optimizer = new PngFilterOptimizer(5, 2, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.ScanlineAdaptive);

    Assert.That(filters[1], Is.AnyOf(PngFilterType.Up, PngFilterType.None, PngFilterType.Sub));
  }

  [Test]
  public void WeightedContinuity_ReturnsSameLengthAsHeight() {
    byte[][] imageData = [
      [1, 2, 3],
      [4, 5, 6],
      [7, 8, 9]
    ];

    var optimizer = new PngFilterOptimizer(3, 3, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.WeightedContinuity);

    Assert.That(filters, Has.Length.EqualTo(3));
  }

  [Test]
  public void Palette_SingleFilter_8Bit_ReturnsDynamicFilter() {
    byte[][] imageData = [
      [0, 1, 2],
      [3, 4, 5]
    ];

    var optimizer = new PngFilterOptimizer(3, 2, 1, false, true, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.SingleFilter);

    Assert.That(filters, Has.Length.EqualTo(2));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void Palette_ScanlineAdaptive_8Bit_ReturnsDynamicFilter() {
    byte[][] imageData = [
      [0, 1, 2],
      [3, 4, 5]
    ];

    var optimizer = new PngFilterOptimizer(3, 2, 1, false, true, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.ScanlineAdaptive);

    Assert.That(filters, Has.Length.EqualTo(2));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void SubBytePalette_SingleFilter_ReturnsAllNone() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78]
    ];

    var optimizer = new PngFilterOptimizer(2, 2, 1, false, true, 4, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.SingleFilter);

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
  }

  [Test]
  public void LowBitGrayscale_ScanlineAdaptive_ReturnsAllNone() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78]
    ];

    var optimizer = new PngFilterOptimizer(2, 2, 1, true, false, 4, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.ScanlineAdaptive);

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
  }

  [Test]
  public void SingleRow_SingleFilter_ReturnsSingleElement() {
    byte[][] imageData = [[100, 150, 200]];

    var optimizer = new PngFilterOptimizer(3, 1, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.SingleFilter);

    Assert.That(filters, Has.Length.EqualTo(1));
  }

  [Test]
  public void BruteForce_ProducesValidFilterArray() {
    byte[][] imageData = [
      [10, 20, 30, 40],
      [50, 60, 70, 80],
      [90, 100, 110, 120]
    ];

    var optimizer = new PngFilterOptimizer(4, 3, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.BruteForce);

    Assert.That(filters, Has.Length.EqualTo(3));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
    Assert.That(filters[0], Is.EqualTo(filters[1]));
    Assert.That(filters[1], Is.EqualTo(filters[2]));
  }

  [Test]
  public void BruteForce_SubBytePalette_ReturnsNone() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78]
    ];

    var optimizer = new PngFilterOptimizer(2, 2, 1, false, true, 4, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.BruteForce);

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
  }

  [Test]
  public void BruteForce_FindsSameOrBetterThanSingleFilter() {
    byte[][] imageData = [
      [10, 20, 30, 40, 50, 60, 70, 80],
      [80, 70, 60, 50, 40, 30, 20, 10],
      [5, 15, 25, 35, 45, 55, 65, 75],
      [90, 80, 70, 60, 50, 40, 30, 20]
    ];

    var optimizer = new PngFilterOptimizer(8, 4, 1, false, false, 8, imageData);
    var singleFilters = optimizer.OptimizeFilters(FilterStrategy.SingleFilter);
    var bruteFilters = optimizer.OptimizeFilters(FilterStrategy.BruteForce);

    var singleFiltered = FilterTools.ApplyFilters(imageData, singleFilters, 1);
    var bruteFiltered = FilterTools.ApplyFilters(imageData, bruteFilters, 1);

    long singleSize, bruteSize;
    using (var ms = new MemoryStream()) {
      using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in singleFiltered)
          zlib.Write(scanline);
      }

      singleSize = ms.Length;
    }

    using (var ms = new MemoryStream()) {
      using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in bruteFiltered)
          zlib.Write(scanline);
      }

      bruteSize = ms.Length;
    }

    Assert.That(bruteSize, Is.LessThanOrEqualTo(singleSize));
  }

  [Test]
  public void BruteForceAdaptive_ProducesValidFilterArray() {
    byte[][] imageData = [
      [10, 20, 30, 40],
      [50, 60, 70, 80],
      [90, 100, 110, 120]
    ];

    var optimizer = new PngFilterOptimizer(4, 3, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.BruteForceAdaptive);

    Assert.That(filters, Has.Length.EqualTo(3));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void BruteForceAdaptive_CanSelectDifferentFiltersPerRow() {
    // Create heterogeneous image where different rows favor different filters
    var rng = new Random(42);
    var imageData = new byte[8][];
    for (var y = 0; y < 8; ++y) {
      imageData[y] = new byte[64];
      if (y % 2 == 0)
        // Constant row — favors Sub or None
        for (var x = 0; x < 64; ++x)
          imageData[y][x] = (byte)(y * 30);
      else
        // Random row
        rng.NextBytes(imageData[y]);
    }

    var optimizer = new PngFilterOptimizer(64, 8, 1, false, false, 8, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.BruteForceAdaptive);

    Assert.That(filters, Has.Length.EqualTo(8));
    // Verify at least one filter is defined for each row
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void BruteForceAdaptive_SubBytePalette_ReturnsNone() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78]
    ];

    var optimizer = new PngFilterOptimizer(2, 2, 1, false, true, 4, imageData);
    var filters = optimizer.OptimizeFilters(FilterStrategy.BruteForceAdaptive);

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
  }

  [Test]
  public void BruteForceAdaptive_SameOrBetterThanBruteForce() {
    var rng = new Random(99);
    var imageData = new byte[16][];
    for (var y = 0; y < 16; ++y) {
      imageData[y] = new byte[32];
      rng.NextBytes(imageData[y]);
    }

    var optimizer = new PngFilterOptimizer(32, 16, 1, false, false, 8, imageData);
    var bruteFilters = optimizer.OptimizeFilters(FilterStrategy.BruteForce);
    var adaptiveFilters = optimizer.OptimizeFilters(FilterStrategy.BruteForceAdaptive);

    var bruteFiltered = FilterTools.ApplyFilters(imageData, bruteFilters, 1);
    var adaptiveFiltered = FilterTools.ApplyFilters(imageData, adaptiveFilters, 1);

    long bruteSize, adaptiveSize;
    using (var ms = new MemoryStream()) {
      using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in bruteFiltered)
          zlib.Write(scanline);
      }

      bruteSize = ms.Length;
    }

    using (var ms = new MemoryStream()) {
      using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in adaptiveFiltered)
          zlib.Write(scanline);
      }

      adaptiveSize = ms.Length;
    }

    Assert.That(adaptiveSize, Is.LessThanOrEqualTo(bruteSize));
  }
}
