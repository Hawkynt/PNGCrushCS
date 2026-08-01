using System;
using FileFormat.Core;
using FileFormat.Pgx;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// PGX, checked against a file the reference tool wrote rather than against our own writer.
/// </summary>
/// <remarks>
/// The bytes below are exactly what ImageMagick produces for a known 4x2 grey ramp. Comparing
/// against them is the same kind of check the vintage formats get from RECOIL: a decoder tested
/// only against its own encoder agrees with itself however wrong it is.
/// </remarks>
[TestFixture]
public sealed class PgxTests {

  /// <summary>"PG ML + 8 4 2\n" and eight samples, as the reference tool writes them.</summary>
  private static byte[] Reference => [
    0x50, 0x47, 0x20, 0x4D, 0x4C, 0x20, 0x2B, 0x20, 0x38, 0x20, 0x34, 0x20, 0x32, 0x0A,
    0x00, 0x40, 0x80, 0xFF, 0x20, 0x60, 0xA0, 0xE0,
  ];

  [Test]
  [Category("Unit")]
  public void ReferenceFile_ReadsBackItsOwnSamples() {
    var file = PgxReader.FromBytes(Reference);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Depth, Is.EqualTo(8));
      Assert.That(file.IsSigned, Is.False);
      Assert.That(file.IsBigEndian, Is.True);
      Assert.That(file.Samples, Is.EqualTo(new byte[] { 0x00, 0x40, 0x80, 0xFF, 0x20, 0x60, 0xA0, 0xE0 }));
    });
  }

  /// <summary>A grey picture must survive being written and read back exactly.</summary>
  [Test]
  [Category("Unit")]
  public void GreyPicture_RoundTripsUnchanged() {
    var rgb = new byte[16 * 9 * 3];
    for (var i = 0; i < 16 * 9; ++i) {
      var level = (byte)(i * 255 / (16 * 9 - 1));
      rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = level;
    }

    var source = new RawImage { Width = 16, Height = 9, Format = PixelFormat.Rgb24, PixelData = rgb };
    var written = PgxWriter.ToBytes(PgxFile.FromRawImage(source));
    var actual = PgxFile.ToRawImage(PgxReader.FromBytes(written));

    Assert.That(actual.PixelData, Is.EqualTo(rgb));
  }

  /// <summary>A sixteen-bit file is two bytes a sample, in the order its header names.</summary>
  [Test]
  [Category("Unit")]
  public void WideSamples_HonourTheStatedByteOrder() {
    // "PG LM + 16 2 1\n" then two little-endian samples: 0x0000 and 0xFFFF.
    byte[] little = [
      0x50, 0x47, 0x20, 0x4C, 0x4D, 0x20, 0x2B, 0x20, 0x31, 0x36, 0x20, 0x32, 0x20, 0x31, 0x0A,
      0x00, 0x00, 0xFF, 0xFF,
    ];

    var file = PgxReader.FromBytes(little);

    Assert.Multiple(() => {
      Assert.That(file.IsBigEndian, Is.False);
      Assert.That(file.Depth, Is.EqualTo(16));
      Assert.That(file.Samples[0], Is.EqualTo(0));
      Assert.That(file.Samples[1], Is.EqualTo(255));
    });
  }
}
