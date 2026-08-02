using System;
using FileFormat.Core;

namespace FileFormat.Tga.Tests;

/// <summary>
/// The two ways a Targa packs five bits a channel, neither of which was read.
/// </summary>
/// <remarks>
/// A colour map of one word an entry was stepped over without being read, leaving the whole palette
/// black and every picture using one with it — and those are common, being what the earliest Targa
/// boards stored. A sixteen-bit picture had no mode of its own either and fell through to the
/// catch-all, which drew it as eight-bit greyscale.
/// <para/>
/// Rows are turned the right way up by the reader, which then says so; it used to report the file's
/// own origin while holding rows it had already reordered, so the conversion turned them over a
/// second time and every bottom-up picture came back upside down.
/// <para/>
/// Checked against ImageMagick on real files: both come back right to within the one step that
/// separates widening five bits by repetition, which is what this library does everywhere, from
/// scaling them by division.
/// </remarks>
[TestFixture]
public sealed class TgaPackedColorTests {

  private static byte[] _Header(int imageType, int width, int height, int bpp, int cmapLength, int cmapDepth, int descriptor) {
    var data = new byte[18];
    data[1] = (byte)(cmapLength > 0 ? 1 : 0);
    data[2] = (byte)imageType;
    data[5] = (byte)cmapLength;
    data[6] = (byte)(cmapLength >> 8);
    data[7] = (byte)cmapDepth;
    data[12] = (byte)width;
    data[13] = (byte)(width >> 8);
    data[14] = (byte)height;
    data[15] = (byte)(height >> 8);
    data[16] = (byte)bpp;
    data[17] = (byte)descriptor;
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesAColourMapOfOneWordAnEntry() {
    // Two entries: pure red and pure blue, five bits a channel in a little-endian word.
    var data = new byte[18 + 4 + 2];
    _Header(1, 2, 1, 8, 2, 15, 0x20).CopyTo(data, 0);
    data[18] = 0x00; data[19] = 0x7C; // 0x7C00 -> red
    data[20] = 0x1F; data[21] = 0x00; // 0x001F -> blue
    data[22] = 0; data[23] = 1;

    var image = TgaFile.ToRawImage(TgaReader.FromBytes(data));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "the first entry is red");
      Assert.That(image.Palette![1], Is.Zero);
      Assert.That(image.Palette![2], Is.Zero);
      Assert.That(image.Palette![5], Is.EqualTo(255), "the second is blue");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_DrawsASixteenBitPictureInColourAndNotInGrey() {
    var data = new byte[18 + 4];
    _Header(2, 2, 1, 16, 0, 0, 0x20).CopyTo(data, 0);
    data[18] = 0x00; data[19] = 0x7C; // red
    data[20] = 0xE0; data[21] = 0x03; // green

    var image = TgaFile.ToRawImage(TgaReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24), "not greyscale");
      Assert.That(image.PixelData[0], Is.EqualTo(255));
      Assert.That(image.PixelData[1], Is.Zero);
      Assert.That(image.PixelData[4], Is.EqualTo(255), "the second pixel is green");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TurnsABottomUpFileTheRightWayUpOnceOnly() {
    // Two rows of one pixel: the file stores the bottom row first.
    var data = new byte[18 + 6];
    _Header(2, 1, 2, 24, 0, 0, 0x00).CopyTo(data, 0);
    data[18] = 1; data[19] = 2; data[20] = 3;   // bottom row
    data[21] = 4; data[22] = 5; data[23] = 6;   // top row

    var file = TgaReader.FromBytes(data);
    var image = TgaFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Origin, Is.EqualTo(TgaOrigin.TopLeft), "the reader says what it holds");
      Assert.That(image.PixelData[0], Is.EqualTo(4), "the top row comes first");
      Assert.That(image.PixelData[3], Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_LeavesATopDownFileAlone() {
    var data = new byte[18 + 6];
    _Header(2, 1, 2, 24, 0, 0, 0x20).CopyTo(data, 0);
    data[18] = 1; data[19] = 2; data[20] = 3;
    data[21] = 4; data[22] = 5; data[23] = 6;

    var image = TgaFile.ToRawImage(TgaReader.FromBytes(data));

    Assert.That(image.PixelData[0], Is.EqualTo(1));
  }
}
