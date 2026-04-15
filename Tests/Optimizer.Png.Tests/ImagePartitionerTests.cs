using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class ImagePartitionerTests {
  [Test]
  public void Palette_8Bit_UsesSmartPartitioning() {
    byte[][] imageData = [
      [0, 1, 2, 3],
      [4, 5, 6, 7],
      [8, 9, 10, 11]
    ];

    var partitioner = new ImagePartitioner(imageData, 3, 1, true, false, 8);
    var (filters, filteredData) = partitioner.OptimizePartitions();

    Assert.That(filters, Has.Length.EqualTo(3));
    Assert.That(filteredData, Has.Length.EqualTo(3));
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);
  }

  [Test]
  public void SubBytePalette_ReturnsAllNoneFilters() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78],
      [0x9A, 0xBC]
    ];

    var partitioner = new ImagePartitioner(imageData, 3, 1, true, false, 4);
    var (filters, filteredData) = partitioner.OptimizePartitions();

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
    Assert.That(filteredData, Has.Length.EqualTo(3));
  }

  [Test]
  public void LowBitGrayscale_ReturnsAllNoneFilters() {
    byte[][] imageData = [
      [0x12, 0x34],
      [0x56, 0x78],
      [0x9A, 0xBC]
    ];

    var partitioner = new ImagePartitioner(imageData, 3, 1, false, true, 4);
    var (filters, filteredData) = partitioner.OptimizePartitions();

    Assert.That(filters, Has.All.EqualTo(PngFilterType.None));
  }

  [Test]
  public void NonPalette_ReturnsFiltersMatchingHeight() {
    byte[][] imageData = [
      [10, 20, 30, 40],
      [50, 60, 70, 80],
      [90, 100, 110, 120],
      [130, 140, 150, 160]
    ];

    var partitioner = new ImagePartitioner(imageData, 4, 1, false, false, 8);
    var (filters, filteredData) = partitioner.OptimizePartitions();

    Assert.That(filters, Has.Length.EqualTo(4));
    Assert.That(filteredData, Has.Length.EqualTo(4));
  }

  [Test]
  public void FilteredData_HasFilterTypeBytePrepended() {
    byte[][] imageData = [
      [10, 20, 30],
      [40, 50, 60]
    ];

    var partitioner = new ImagePartitioner(imageData, 2, 1, true, false, 8);
    var (_, filteredData) = partitioner.OptimizePartitions();

    foreach (var row in filteredData)
      Assert.That(row.Length, Is.EqualTo(imageData[0].Length + 1));
  }

  [Test]
  public void SmartPartitioning_DoesNotExceedFilterTypeRange() {
    var imageData = new byte[20][];
    for (var y = 0; y < 20; ++y) {
      imageData[y] = new byte[32];
      for (var x = 0; x < 32; ++x)
        imageData[y][x] = (byte)((y * 32 + x) % 256);
    }

    var partitioner = new ImagePartitioner(imageData, 20, 3, false, false, 8);
    var (filters, _) = partitioner.OptimizePartitions();

    foreach (var filter in filters) {
      Assert.That((int)filter, Is.GreaterThanOrEqualTo(0));
      Assert.That((int)filter, Is.LessThanOrEqualTo(4));
    }
  }

  [Test]
  public void CustomParams_AreRespected() {
    var imageData = new byte[10][];
    for (var y = 0; y < 10; ++y) {
      imageData[y] = new byte[8];
      for (var x = 0; x < 8; ++x)
        imageData[y][x] = (byte)(y * 10 + x);
    }

    var customParams = new SmartPartitioningParams(3, 1, 1.05, 1.2);
    var partitioner = new ImagePartitioner(imageData, 10, 1, false, false, 8, customParams);
    var (filters, filteredData) = partitioner.OptimizePartitions();

    Assert.That(filters, Has.Length.EqualTo(10));
    Assert.That(filteredData, Has.Length.EqualTo(10));
  }
}
