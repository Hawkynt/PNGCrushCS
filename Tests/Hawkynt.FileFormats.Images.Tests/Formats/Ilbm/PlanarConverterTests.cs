using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class PlanarConverterTests {

  [Test]
  [Category("Unit")]
  public void PlanarToChunky_1Plane_Monochrome() {
    // 8 pixels wide, 1 row, 1 plane
    // Bit pattern: 10110001 = pixels 1,0,1,1,0,0,0,1
    var planar = new byte[] { 0b10110001 };
    // bytesPerPlaneRow = ceil(8/16)*2 = 2, but only first byte matters
    var fullPlanar = new byte[2]; // word-aligned
    fullPlanar[0] = 0b10110001;

    var chunky = PlanarConverter.PlanarToChunky(fullPlanar, 8, 1, 1);

    Assert.That(chunky, Is.EqualTo(new byte[] { 1, 0, 1, 1, 0, 0, 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void PlanarToChunky_4Planes_16Colors() {
    // 8 pixels wide, 1 row, 4 planes
    // bytesPerPlaneRow = ceil(8/16)*2 = 2
    // Set pixel 0 to color 15 (all bits set) and pixel 7 to color 1 (only plane 0)
    var bytesPerPlaneRow = 2;
    var planar = new byte[bytesPerPlaneRow * 4]; // 4 planes
    // Plane 0: bit 7 set for pixel 0, bit 0 set for pixel 7
    planar[0 * bytesPerPlaneRow] = 0b10000001;
    // Plane 1: bit 7 set for pixel 0
    planar[1 * bytesPerPlaneRow] = 0b10000000;
    // Plane 2: bit 7 set for pixel 0
    planar[2 * bytesPerPlaneRow] = 0b10000000;
    // Plane 3: bit 7 set for pixel 0
    planar[3 * bytesPerPlaneRow] = 0b10000000;

    var chunky = PlanarConverter.PlanarToChunky(planar, 8, 1, 4);

    Assert.Multiple(() => {
      Assert.That(chunky[0], Is.EqualTo(15)); // all 4 planes set
      Assert.That(chunky[7], Is.EqualTo(1));  // only plane 0 set
      Assert.That(chunky[1], Is.EqualTo(0));  // no planes set
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_ChunkyToPlanarToChunky() {
    var width = 16;
    var height = 4;
    var numPlanes = 4;
    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % (1 << numPlanes));

    var planar = PlanarConverter.ChunkyToPlanar(chunky, width, height, numPlanes);
    var restored = PlanarConverter.PlanarToChunky(planar, width, height, numPlanes);

    Assert.That(restored, Is.EqualTo(chunky));
  }

  [Test]
  [Category("Unit")]
  public void PlanarToChunky_KnownPixelValues() {
    // 4 pixels wide, 1 row, 2 planes
    // bytesPerPlaneRow = ceil(4/16)*2 = 2
    var bytesPerPlaneRow = 2;
    var planar = new byte[bytesPerPlaneRow * 2];
    // Plane 0: bits 7654 = 1010 (pixels: 1,0,1,0 for plane 0)
    planar[0] = 0b10100000;
    // Plane 1: bits 7654 = 0110 (pixels: 0,1,1,0 for plane 1)
    planar[bytesPerPlaneRow] = 0b01100000;

    var chunky = PlanarConverter.PlanarToChunky(planar, 4, 1, 2);

    // pixel 0: plane0=1, plane1=0 -> 1
    // pixel 1: plane0=0, plane1=1 -> 2
    // pixel 2: plane0=1, plane1=1 -> 3
    // pixel 3: plane0=0, plane1=0 -> 0
    Assert.That(chunky, Is.EqualTo(new byte[] { 1, 2, 3, 0 }));
  }
}
