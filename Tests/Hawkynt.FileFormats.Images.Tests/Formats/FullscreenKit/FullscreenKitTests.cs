using System;
using System.IO;
using FileFormat.Core;
using FileFormat.FullscreenKit;

namespace FileFormat.FullscreenKit.Tests;

/// <summary>
/// Fullscreen Construction Kit, an overscan picture whose rows reserve more than they use.
/// </summary>
/// <remarks>
/// These were written against two variants the format does not have — 416 by 274 and 448 by 272 —
/// and a header that was the palette alone. It is 448 by 274 behind two marker bytes, and its rows
/// are 230 bytes where the pixels need 224. Fixtures are built through the writer, since none of
/// that can be assembled by hand without restating the writer's own arithmetic.
/// </remarks>
[TestFixture]
public sealed class FullscreenKitTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => FullscreenKitReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongLength_Throws()
    => Assert.Throws<InvalidDataException>(() => FullscreenKitReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMarker_Throws()
    => Assert.Throws<InvalidDataException>(() => FullscreenKitReader.FromBytes(new byte[FullscreenKitFile.FileSize]));

  [Test]
  [Category("Unit")]
  public void FileSize_CountsTheReservedBytesInEveryRow() {
    Assert.Multiple(() => {
      Assert.That(FullscreenKitFile.Stride, Is.EqualTo(230));
      Assert.That(FullscreenKitFile.FileSize, Is.EqualTo(63054));

      // The pixels of a row need 224 bytes; the format reserves six more.
      Assert.That(FullscreenKitFile.PixelWidth / 8 * FullscreenKitFile.NumPlanes, Is.EqualTo(224));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsThePicture() {
    var source = _Sample();

    var restored = FullscreenKitFile.ToRawImage(
      FullscreenKitReader.FromBytes(FullscreenKitWriter.ToBytes(FullscreenKitFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(FullscreenKitFile.PixelWidth));
      Assert.That(restored.Height, Is.EqualTo(FullscreenKitFile.PixelHeight));
      Assert.That(restored.PixelData, Has.Length.EqualTo(FullscreenKitFile.PixelWidth * FullscreenKitFile.PixelHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongSize_Throws()
    => Assert.Throws<ArgumentException>(() => FullscreenKitFile.FromRawImage(new() {
      Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3],
    }));

  private static RawImage _Sample() {
    var width = FullscreenKitFile.PixelWidth;
    var height = FullscreenKitFile.PixelHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 255 / (width - 1));
      rgb[at + 1] = (byte)(y * 255 / (height - 1));
      rgb[at + 2] = (byte)((x / 16 + y / 16) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
