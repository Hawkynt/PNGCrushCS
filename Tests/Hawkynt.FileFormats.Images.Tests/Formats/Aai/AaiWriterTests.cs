using System;
using FileFormat.Core;

namespace FileFormat.Aai.Tests;

/// <summary>
/// The channel order AAI stores on disk.
/// </summary>
/// <remarks>
/// AAI has no public specification; ImageMagick's <c>coders/aai.c</c> is the reference other
/// readers agree with, and it reads and writes each pixel as blue, green, red, then alpha. A
/// reader and a writer that agree with each other but not with that order would still pass a
/// round trip, which is why this pins the byte layout directly rather than only checking that
/// encoding and decoding undo each other.
/// </remarks>
[TestFixture]
public sealed class AaiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesChannelsInBgraOrder() {
    var file = new AaiFile {
      Width = 1,
      Height = 1,
      PixelData = [0x11, 0x22, 0x33, 0x44], // B, G, R, A
    };

    var bytes = AaiWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo(0x11), "blue");
    Assert.That(bytes[9], Is.EqualTo(0x22), "green");
    Assert.That(bytes[10], Is.EqualTo(0x33), "red");
    Assert.That(bytes[11], Is.EqualTo(0x44), "alpha");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_StoresPixelDataInBgraOrder() {
    // A pure red pixel: R=255, everything else 0.
    var red = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 255],
    };

    var file = AaiFile.FromRawImage(red);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "expected B, G, R, A");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReadsPixelDataAsBgraOrder() {
    // On-disk bytes B=0, G=0, R=255, A=255: a pure red pixel.
    var file = new AaiFile { Width = 1, Height = 1, PixelData = [0, 0, 255, 255] };

    var image = AaiFile.ToRawImage(file);
    var rgba = PixelConverter.Convert(image, PixelFormat.Rgba32).PixelData;

    Assert.That(rgba, Is.EqualTo(new byte[] { 255, 0, 0, 255 }), "expected R, G, B, A");
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_RoundTripsAPictureUnchanged() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 255, 0, 0, 255, 255], // red, then blue
    };

    var written = AaiFile.FromRawImage(source);
    var bytes = AaiWriter.ToBytes(written);
    var read = AaiReader.FromBytes(bytes);
    var decoded = AaiFile.ToRawImage(read);
    var rgba = PixelConverter.Convert(decoded, PixelFormat.Rgba32).PixelData;

    Assert.That(rgba, Is.EqualTo(source.PixelData));
  }
}
