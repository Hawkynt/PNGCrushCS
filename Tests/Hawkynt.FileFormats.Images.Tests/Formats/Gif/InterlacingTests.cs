using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

[TestFixture]
public sealed class InterlacingTests {

  [Test]
  public void RoundTrip_InterlacedFlagPreserved() {
    var palette = new byte[] { 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };
    // 8-row image so the 4-pass de-interlacer has all passes engaged.
    var pixels = new byte[8 * 4];
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 4; ++x) pixels[y * 4 + x] = (byte)(y % 4);

    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 4, Height: 8, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 1, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = palette,
      Frames = [new Frame {
        Left = 0, Top = 0, Width = 4, Height = 8,
        PixelData = pixels,
        IsInterlaced = true,
      }],
    };

    var encoded = GifWriter.ToBytes(src);
    var decoded = GifReader.FromBytes(encoded);

    Assert.That(decoded.Frames[0].IsInterlaced, Is.True, "interlace flag preserved");
    Assert.That(decoded.Frames[0].PixelData, Is.EqualTo(pixels),
      "pixels should round-trip identically through the de-interlacer + interlacer pair");
  }
}
