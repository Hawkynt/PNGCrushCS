using System;
using FileFormat.IffAcbm;

namespace FileFormat.IffAcbm.Tests;

[TestFixture]
public sealed class PlanarConverterTests {

  [Test]
  [Category("Unit")]
  public void ContiguousPlanarToChunky_AllZeros_ReturnsAllZeros() {
    var bytesPerPlaneRow = 2; // (8+15)/16*2 = 2
    var planar = new byte[bytesPerPlaneRow * 2 * 2]; // 2 planes, 2 rows
    var result = IffAcbmReader._ContiguousPlanarToChunky(planar, 8, 2, 2);

    Assert.That(result, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ChunkyToContiguousPlanar_AllZeros_ReturnsAllZeros() {
    var chunky = new byte[8 * 2];
    var result = IffAcbmWriter._ChunkyToContiguousPlanar(chunky, 8, 2, 2);

    Assert.That(result, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PlanarToChunkyToplanar() {
    var width = 16;
    var height = 4;
    var numPlanes = 4;
    var numColors = 1 << numPlanes;

    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % numColors);

    var planar = IffAcbmWriter._ChunkyToContiguousPlanar(chunky, width, height, numPlanes);
    var restored = IffAcbmReader._ContiguousPlanarToChunky(planar, width, height, numPlanes);

    Assert.That(restored, Is.EqualTo(chunky));
  }

  [Test]
  [Category("Unit")]
  public void WordAlignment_NonMultipleOf16Width() {
    var width = 13;
    var height = 2;
    var numPlanes = 2;
    var bytesPerPlaneRow = ((width + 15) / 16) * 2; // = 2

    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % 4);

    var planar = IffAcbmWriter._ChunkyToContiguousPlanar(chunky, width, height, numPlanes);
    Assert.That(planar.Length, Is.EqualTo(numPlanes * bytesPerPlaneRow * height));

    var restored = IffAcbmReader._ContiguousPlanarToChunky(planar, width, height, numPlanes);
    Assert.That(restored, Is.EqualTo(chunky));
  }

  [Test]
  [Category("Unit")]
  public void ContiguousLayout_PlanesAreContiguous() {
    // With 1 plane, pixel index 0 at position 0 and pixel index 1 at position 1
    // Width = 8, height = 1, 1 plane
    var chunky = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
    var planar = IffAcbmWriter._ChunkyToContiguousPlanar(chunky, 8, 1, 1);

    // plane 0: bits for 1,0,1,0,1,0,1,0 => 10101010 binary = 0xAA
    var bytesPerPlaneRow = ((8 + 15) / 16) * 2; // = 2
    Assert.Multiple(() => {
      Assert.That(planar.Length, Is.EqualTo(bytesPerPlaneRow * 1)); // 1 plane * 1 row
      Assert.That(planar[0], Is.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void ContiguousLayout_MultiPlane_DataSeparated() {
    // 2 planes, width=8, height=1
    // Pixel value 3 (binary 11) = bit set in both planes
    var chunky = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0 };
    var planar = IffAcbmWriter._ChunkyToContiguousPlanar(chunky, 8, 1, 2);

    var bytesPerPlaneRow = 2; // word-aligned
    var bytesPerPlane = bytesPerPlaneRow * 1;

    Assert.Multiple(() => {
      // Plane 0: bit 0 of value 3 = 1, rest = 0 => 10000000 = 0x80
      Assert.That(planar[0], Is.EqualTo(0x80));
      // Plane 1: bit 1 of value 3 = 1, rest = 0 => 10000000 = 0x80
      Assert.That(planar[bytesPerPlane], Is.EqualTo(0x80));
    });
  }
}
