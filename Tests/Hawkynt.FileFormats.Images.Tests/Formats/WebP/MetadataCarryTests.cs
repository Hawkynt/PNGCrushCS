using System;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Png;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Metadata travelling through a WebP and out the other side into another format.
/// </summary>
/// <remarks>
/// The reader already kept the EXIF, XMP and ICC chunks as raw bytes and the writer already emitted
/// them, so a WebP read and written here never lost them. What was missing was the bridge to the
/// interchange model: without it the metadata could not leave a WebP for any other format, which is
/// most of what the model exists to do.
/// </remarks>
[TestFixture]
public sealed class MetadataCarryTests {

  private static RawImage _Picture(ImageMetadata? metadata) {
    var pixels = new byte[9 * 5 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11);

    return new() { Width = 9, Height = 5, Format = PixelFormat.Rgb24, PixelData = pixels, Metadata = metadata };
  }

  private static ImageMetadata _Sample() => new() {
    XmpPacket = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'><test/></x:xmpmeta>"),
    IccProfile = [1, 2, 3, 4, 5, 6, 7, 8],
  };

  [Test]
  [Category("Integration")]
  public void WhatGoesIntoAWebPComesOutOfIt() {
    var written = WebPWriter.ToBytes(WebPFile.FromRawImage(_Picture(_Sample())));

    var decoded = WebPFile.ToRawImage(WebPReader.FromBytes(written));

    Assert.That(decoded.Metadata, Is.Not.Null, "a WebP written with metadata carries none back");
    Assert.Multiple(() => {
      Assert.That(decoded.Metadata!.XmpPacket, Is.EqualTo(_Sample().XmpPacket));
      Assert.That(decoded.Metadata.IccProfile, Is.EqualTo(_Sample().IccProfile));
    });
  }

  [Test]
  [Category("Integration")]
  public void MetadataCrossesFromWebPIntoPng() {
    // The whole point of the model: what one format carries, another can be handed.
    var webp = WebPWriter.ToBytes(WebPFile.FromRawImage(_Picture(_Sample())));
    var viaWebP = WebPFile.ToRawImage(WebPReader.FromBytes(webp));

    var png = PngWriter.ToBytes(PngFile.FromRawImage(viaWebP));
    var viaPng = PngFile.ToRawImage(PngReader.FromBytes(png));

    Assert.That(viaPng.Metadata, Is.Not.Null, "the metadata did not survive the hop into PNG");
    Assert.That(viaPng.Metadata!.XmpPacket, Is.EqualTo(_Sample().XmpPacket));
  }

  [Test]
  [Category("Unit")]
  public void TheChunksAreNamedTheWayTheFormatNamesThem() {
    var chunks = WebPMetadataCodec.Write(_Sample());

    // "XMP " with its trailing space is the name, not a typo — a four-character code always is.
    Assert.Multiple(() => {
      Assert.That(chunks.Select(c => c.ChunkId), Does.Contain("XMP "));
      Assert.That(chunks.Select(c => c.ChunkId), Does.Contain("ICCP"));
    });
  }

  [Test]
  [Category("Unit")]
  public void NoMetadataMeansNoChunks()
    => Assert.That(WebPMetadataCodec.Write(null), Is.Empty);
}
