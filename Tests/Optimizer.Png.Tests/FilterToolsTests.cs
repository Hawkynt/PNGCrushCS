using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class FilterToolsTests {
  [Test]
  public void ApplyFilter_None_ReturnsSameData() {
    byte[] scanline = [10, 20, 30, 40, 50];
    var result = FilterTools.ApplyFilter(PngFilterType.None, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result, Is.EqualTo(scanline));
  }

  [Test]
  public void ApplyFilter_Sub_SubtractsLeftNeighbor() {
    byte[] scanline = [10, 20, 35, 55];
    var result = FilterTools.ApplyFilter(PngFilterType.Sub, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result[0], Is.EqualTo(10));
    Assert.That(result[1], Is.EqualTo((byte)(20 - 10)));
    Assert.That(result[2], Is.EqualTo((byte)(35 - 20)));
    Assert.That(result[3], Is.EqualTo((byte)(55 - 35)));
  }

  [Test]
  public void ApplyFilter_Sub_WithMultiBytesPerPixel() {
    byte[] scanline = [1, 2, 3, 4, 5, 6];
    var result = FilterTools.ApplyFilter(PngFilterType.Sub, scanline, ReadOnlySpan<byte>.Empty, 3);
    Assert.That(result[0], Is.EqualTo(1));
    Assert.That(result[1], Is.EqualTo(2));
    Assert.That(result[2], Is.EqualTo(3));
    Assert.That(result[3], Is.EqualTo((byte)(4 - 1)));
    Assert.That(result[4], Is.EqualTo((byte)(5 - 2)));
    Assert.That(result[5], Is.EqualTo((byte)(6 - 3)));
  }

  [Test]
  public void ApplyFilter_Up_SubtractsPreviousScanline() {
    byte[] scanline = [100, 200, 50];
    byte[] previous = [30, 40, 50];
    var result = FilterTools.ApplyFilter(PngFilterType.Up, scanline, previous, 1);
    Assert.That(result[0], Is.EqualTo((byte)(100 - 30)));
    Assert.That(result[1], Is.EqualTo((byte)(200 - 40)));
    Assert.That(result[2], Is.EqualTo((byte)(50 - 50)));
  }

  [Test]
  public void ApplyFilter_Up_NoPreviousScanline_ReturnsSameData() {
    byte[] scanline = [10, 20, 30];
    var result = FilterTools.ApplyFilter(PngFilterType.Up, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result, Is.EqualTo(scanline));
  }

  [Test]
  public void ApplyFilter_Average_ComputesCorrectly() {
    byte[] scanline = [10, 20, 30];
    byte[] previous = [20, 40, 60];
    var result = FilterTools.ApplyFilter(PngFilterType.Average, scanline, previous, 1);
    Assert.That(result[0], Is.EqualTo(unchecked((byte)(10 - (20 >> 1)))));
    Assert.That(result[1], Is.EqualTo(unchecked((byte)(20 - ((10 + 40) >> 1)))));
    Assert.That(result[2], Is.EqualTo(unchecked((byte)(30 - ((20 + 60) >> 1)))));
  }

  [Test]
  public void ApplyFilter_Average_NoPreviousScanline() {
    byte[] scanline = [10, 20, 30];
    var result = FilterTools.ApplyFilter(PngFilterType.Average, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result[0], Is.EqualTo(10));
    Assert.That(result[1], Is.EqualTo((byte)(20 - (10 >> 1))));
    Assert.That(result[2], Is.EqualTo((byte)(30 - (20 >> 1))));
  }

  [Test]
  public void ApplyFilter_Paeth_ComputesCorrectly() {
    byte[] scanline = [10, 20, 30, 40];
    byte[] previous = [5, 10, 15, 20];
    var result = FilterTools.ApplyFilter(PngFilterType.Paeth, scanline, previous, 1);
    Assert.That(result[0], Is.EqualTo((byte)(10 - 5)));
    var a1 = 10;
    var b1 = 10;
    var c1 = 5;
    var p1 = a1 + b1 - c1;
    var pa1 = Math.Abs(p1 - a1);
    var pb1 = Math.Abs(p1 - b1);
    var pc1 = Math.Abs(p1 - c1);
    var pr1 = pa1 <= pb1 && pa1 <= pc1 ? a1 : pb1 <= pc1 ? b1 : c1;
    Assert.That(result[1], Is.EqualTo((byte)(20 - pr1)));
  }

  [Test]
  public void ApplyFilter_Paeth_NoPreviousScanline_EqualsSubFilter() {
    byte[] scanline = [10, 20, 30, 40];
    var resultPaeth = FilterTools.ApplyFilter(PngFilterType.Paeth, scanline, ReadOnlySpan<byte>.Empty, 1);
    var resultSub = FilterTools.ApplyFilter(PngFilterType.Sub, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(resultPaeth, Is.EqualTo(resultSub));
  }

  [Test]
  public void ApplyFilters_MultiRow_AppliesCorrectFilterPerRow() {
    byte[][] imageData = [
      [10, 20, 30],
      [40, 50, 60],
      [70, 80, 90]
    ];
    PngFilterType[] filters = [PngFilterType.None, PngFilterType.Up, PngFilterType.Sub];

    var result = FilterTools.ApplyFilters(imageData, filters, 1);

    Assert.That(result.Length, Is.EqualTo(3));
    Assert.That(result[0][0], Is.EqualTo((byte)PngFilterType.None));
    Assert.That(result[1][0], Is.EqualTo((byte)PngFilterType.Up));
    Assert.That(result[2][0], Is.EqualTo((byte)PngFilterType.Sub));

    Assert.That(result[0][1], Is.EqualTo(10));
    Assert.That(result[0][2], Is.EqualTo(20));
    Assert.That(result[0][3], Is.EqualTo(30));

    Assert.That(result[1][1], Is.EqualTo((byte)(40 - 10)));
    Assert.That(result[1][2], Is.EqualTo((byte)(50 - 20)));
    Assert.That(result[1][3], Is.EqualTo((byte)(60 - 30)));

    Assert.That(result[2][1], Is.EqualTo(70));
    Assert.That(result[2][2], Is.EqualTo((byte)(80 - 70)));
    Assert.That(result[2][3], Is.EqualTo((byte)(90 - 80)));
  }

  [Test]
  public void ApplyFilters_OutputLengthIsInputPlusOne() {
    byte[][] imageData = [[1, 2, 3, 4]];
    PngFilterType[] filters = [PngFilterType.None];
    var result = FilterTools.ApplyFilters(imageData, filters, 1);
    Assert.That(result[0].Length, Is.EqualTo(5));
  }

  [Test]
  public void ApplyFilter_InvalidPngFilterType_ThrowsArgumentOutOfRange() {
    byte[] scanline = [1, 2, 3];
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      FilterTools.ApplyFilter((PngFilterType)99, scanline, ReadOnlySpan<byte>.Empty, 1));
  }

  [Test]
  public void ApplyFilter_SingleByteScanline_AllFilters() {
    byte[] scanline = [42];
    foreach (var filterType in Enum.GetValues<PngFilterType>()) {
      var result = FilterTools.ApplyFilter(filterType, scanline, ReadOnlySpan<byte>.Empty, 1);
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0], Is.EqualTo(42));
    }
  }

  [Test]
  public void ApplyFilter_Sub_ByteWrapsAround() {
    byte[] scanline = [200, 10];
    var result = FilterTools.ApplyFilter(PngFilterType.Sub, scanline, ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result[1], Is.EqualTo(unchecked((byte)(10 - 200))));
  }

  [Test]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  public void Paeth_LargeScanline_SimdMatchesScalar(int bytesPerPixel) {
    var rng = new Random(123);
    var scanline = new byte[1024];
    var previousScanline = new byte[1024];
    rng.NextBytes(scanline);
    rng.NextBytes(previousScanline);

    // Compute expected result using scalar Paeth predictor
    var expected = new byte[scanline.Length];
    for (var i = 0; i < bytesPerPixel; ++i)
      expected[i] = (byte)(scanline[i] - previousScanline[i]);

    for (var i = bytesPerPixel; i < scanline.Length; ++i) {
      var a = scanline[i - bytesPerPixel] & 0xFF;
      var b = previousScanline[i] & 0xFF;
      var c = previousScanline[i - bytesPerPixel] & 0xFF;
      var p = a + b - c;
      var pa = Math.Abs(p - a);
      var pb = Math.Abs(p - b);
      var pc = Math.Abs(p - c);
      var pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      expected[i] = (byte)(scanline[i] - pr);
    }

    var actual = FilterTools.ApplyFilter(PngFilterType.Paeth, scanline, previousScanline, bytesPerPixel);
    Assert.That(actual, Is.EqualTo(expected));
  }
}
