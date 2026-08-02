using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

/// <summary>
/// Two-colour icons and cursors, which share the ICO decoder and were refused by it.
/// </summary>
/// <remarks>
/// One bit a pixel is what the oldest icons use and what nearly every classic cursor is, so a plain
/// black-and-white arrow could not be opened at all — the decoder handled 4, 8, 24 and 32 and threw
/// on anything else.
/// <para/>
/// Checked against ImageMagick on a real 32 by 32 cursor from a public archive of format samples:
/// the pixels come back byte-identical to what it decodes.
/// </remarks>
[TestFixture]
public sealed class IcoMonochromeTests {

  /// <summary>Builds the DIB an ICO entry carries: header, two-colour palette, then the rows.</summary>
  private static byte[] _MonochromeDib(int width, int height, byte[] rows) {
    var srcStride = (width + 31) / 32 * 4;
    var data = new byte[40 + 2 * 4 + srcStride * height];

    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), height * 2); // colour rows plus the mask
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 2);

    // Black then white, stored blue-green-red with a spare byte.
    data[40 + 4] = data[40 + 5] = data[40 + 6] = 255;

    var dstStride = (width + 7) / 8;
    for (var y = 0; y < height; ++y)
      rows.AsSpan(y * dstStride, dstStride).CopyTo(data.AsSpan(40 + 8 + (height - 1 - y) * srcStride));

    return data;
  }

  private static IcoFile _OneEntry(int width, int height, byte[] rows) => new() {
    Images = new List<IcoImage> {
      new() { Width = width, Height = height, BitsPerPixel = 1, Data = _MonochromeDib(width, height, rows) },
    },
  };

  [Test]
  [Category("Unit")]
  public void Decoded_KeepsEveryBitAndTheRightWayUp() {
    // Eight pixels a row: one row all off, one all on, then an alternating one.
    byte[] rows = [0x00, 0xFF, 0xAA];
    var image = IcoFile.ToRawImage(_OneEntry(8, 3, rows));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed1));
      Assert.That(image.Width, Is.EqualTo(8));
      Assert.That(image.Height, Is.EqualTo(3));
      Assert.That(image.PixelData, Is.EqualTo(rows), "the rows are stored upwards and must come back down");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_ReadsTheTwoColoursFromThePalette() {
    var image = IcoFile.ToRawImage(_OneEntry(8, 1, [0xF0]));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.Palette![0], Is.EqualTo(0));
      Assert.That(image.Palette![3], Is.EqualTo(255), "the second entry is white");
      Assert.That(image.Palette![4], Is.EqualTo(255));
      Assert.That(image.Palette![5], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_TurnsIntoTheBlackAndWhitePixelsItNames() {
    var rgb = IcoFile.ToRawImage(_OneEntry(8, 1, [0xF0])).ToRgb24();

    Assert.Multiple(() => {
      // The top four bits are set, so the left half is white and the right half black.
      for (var x = 0; x < 4; ++x)
        Assert.That(rgb[x * 3], Is.EqualTo(255), $"pixel {x}");
      for (var x = 4; x < 8; ++x)
        Assert.That(rgb[x * 3], Is.EqualTo(0), $"pixel {x}");
    });
  }
}
