using System;
using FileFormat.Core;
using FileFormat.Sf3;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>SF3, checked against files the reference tool wrote rather than against our own writer.</summary>
[TestFixture]
public sealed class Sf3Tests {

  /// <summary>A one-channel 4x2 grey ramp, exactly as ImageMagick writes it.</summary>
  private static byte[] Grey => [
    0x81, 0x53, 0x46, 0x33, 0x00, 0xE0, 0xD0, 0x0D, 0x0A, 0x0A, 0x03, 0xBB, 0x09, 0x1D, 0x4E, 0x00,
    0x04, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x11,
    0x00, 0x40, 0x80, 0xFF, 0x20, 0x60, 0xA0, 0xE0,
  ];

  /// <summary>The same picture at sixteen bits a sample, which stores two bytes each.</summary>
  private static byte[] Wide => [
    0x81, 0x53, 0x46, 0x33, 0x00, 0xE0, 0xD0, 0x0D, 0x0A, 0x0A, 0x03, 0x37, 0xB3, 0xAA, 0x51, 0x00,
    0x04, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x12,
    0x00, 0x00, 0x40, 0x40, 0x80, 0x80, 0xFF, 0xFF, 0x20, 0x20, 0x60, 0x60, 0xA0, 0xA0, 0xE0, 0xE0,
  ];

  [Test]
  [Category("Unit")]
  public void ReferenceGreyFile_ReadsBackItsOwnSamples() {
    var file = Sf3Reader.FromBytes(Grey);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Channels, Is.EqualTo(1));
      Assert.That(file.Samples, Is.EqualTo(new byte[] { 0x00, 0x40, 0x80, 0xFF, 0x20, 0x60, 0xA0, 0xE0 }));
    });
  }

  /// <summary>A wide file must narrow to the same picture the eight-bit one holds.</summary>
  [Test]
  [Category("Unit")]
  public void SixteenBitFile_NarrowsToTheSamePicture() {
    var narrow = Sf3File.ToRawImage(Sf3Reader.FromBytes(Grey));
    var wide = Sf3File.ToRawImage(Sf3Reader.FromBytes(Wide));

    Assert.That(wide.PixelData, Is.EqualTo(narrow.PixelData));
  }

  /// <summary>A colour picture must survive being written and read back exactly.</summary>
  [Test]
  [Category("Unit")]
  public void ColourPicture_RoundTripsUnchanged() {
    var rgb = new byte[5 * 3 * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 7 + 11);

    var source = new RawImage { Width = 5, Height = 3, Format = PixelFormat.Rgb24, PixelData = rgb };
    var actual = Sf3File.ToRawImage(Sf3Reader.FromBytes(Sf3Writer.ToBytes(Sf3File.FromRawImage(source))));

    Assert.That(actual.PixelData, Is.EqualTo(rgb));
  }
}
