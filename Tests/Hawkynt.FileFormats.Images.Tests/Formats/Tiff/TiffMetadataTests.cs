using System;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

/// <summary>
/// A TIFF keeps its metadata in its own IFD tags, so it needs no container of its own.
/// </summary>
[TestFixture]
public sealed class TiffMetadataTests {

  private static RawImage _Picture(ImageMetadata? metadata = null) {
    var pixels = new byte[16 * 8 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)i;

    return new() { Width = 16, Height = 8, Format = PixelFormat.Rgb24, PixelData = pixels, Metadata = metadata };
  }

  private static ImageMetadata _Sample() => new() {
    DpiX = 300,
    DpiY = 300,
    TextEntries = [
      new("Description", "A gradient"),
      new("Software", "CrushTest"),
      new("Author", "Hawkynt"),
      new("Copyright", "none"),
    ],
  };

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsTheMetadataItWasGiven()
    => Assert.That(TiffFile.FromRawImage(_Picture(_Sample())).Metadata, Is.Not.Null);

  [Test]
  [Category("Unit")]
  public void WrittenBytesActuallyContainTheText() {
    var written = TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture(_Sample())));
    var text = Encoding.Latin1.GetString(written);

    Assert.That(text, Does.Contain("Hawkynt"));
  }

  [Test]
  [Category("Integration")]
  public void TextAndDensitySurviveAWrittenTiff() {
    var written = TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture(_Sample())));

    var read = TiffMetadataCodec.Read(written);

    Assert.That(read, Is.Not.Null, "a TIFF written with metadata carries none back");
    Assert.Multiple(() => {
      Assert.That(read!.DpiX, Is.EqualTo(300).Within(0.01));
      Assert.That(read.DpiY, Is.EqualTo(300).Within(0.01));
      Assert.That(TiffMetadataCodec.TextFor(read, "Author"), Is.EqualTo("Hawkynt"));
      Assert.That(TiffMetadataCodec.TextFor(read, "Software"), Is.EqualTo("CrushTest"));
      Assert.That(TiffMetadataCodec.TextFor(read, "Description"), Is.EqualTo("A gradient"));
      Assert.That(TiffMetadataCodec.TextFor(read, "Copyright"), Is.EqualTo("none"));
    });
  }

  [Test]
  [Category("Integration")]
  public void MetadataReachesTheRawImageAndComesBackOut() {
    // The whole point of the model: what one format carries, another can be handed.
    var written = TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture(_Sample())));

    var decoded = TiffFile.ToRawImage(TiffReader.FromBytes(written));

    Assert.That(decoded.Metadata, Is.Not.Null);
    Assert.That(TiffMetadataCodec.TextFor(decoded.Metadata!, "Author"), Is.EqualTo("Hawkynt"));
  }

  [Test]
  [Category("Integration")]
  public void XmpSurvives() {
    var xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'></x:xmpmeta>");
    var written = TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture(new() { XmpPacket = xmp })));

    var read = TiffMetadataCodec.Read(written);

    Assert.That(read?.XmpPacket, Is.EqualTo(xmp));
  }

  [Test]
  [Category("Unit")]
  public void AnAspectRatioIsNotADensity() {
    // Unit 1 says the resolution pair is a ratio and not a measurement, so no DPI is invented.
    var written = TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture()));

    Assert.That(TiffMetadataCodec.Read(written)?.DpiX, Is.Null);
  }
}
