using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

/// <summary>
/// Icons whose entry carries the oldest information header, twelve bytes of sixteen-bit fields.
/// </summary>
/// <remarks>
/// The reader admitted a twelve-byte BITMAPCOREHEADER and then read it at BITMAPINFOHEADER offsets.
/// Nothing failed: the sixteen-bit width and height were taken together as one thirty-two-bit
/// number, so an 8 by 16 header reported a width of 1048584, and the depth was read from offset 14,
/// which in a core header is already the first palette entry. Further on, the colour table was
/// walked four bytes an entry where a core header spends three, which does not throw either — it
/// just shifts every colour after the first.
/// <para/>
/// The fixtures here are written out byte by byte rather than produced by this library's own
/// writer, because a picture round-tripped through our own two halves cannot show a mistake they
/// both make. Both were checked against Windows' own icon handling through GDI+
/// (<c>System.Drawing.Icon</c>): it reads the four-bit fixture as 8 by 8 with green at the top
/// left, blue at the bottom right and red everywhere else, and the one-bit fixture as 8 by 8 with
/// the top row white on its left half and the bottom row white on its right — in both cases
/// exactly what the bytes say.
/// </remarks>
[TestFixture]
public sealed class IcoCoreHeaderTests {

  private const int _CoreHeaderSize = 12;
  private const int _DirectorySize = 22;

  /// <summary>Wraps one payload in an ICONDIR and its single entry.</summary>
  private static byte[] _OneEntryIcon(byte width, byte height, byte colourCount, ushort bitCount, byte[] payload) {
    var data = new byte[_DirectorySize + payload.Length];

    // ICONDIR: reserved, type 1 for an icon, one entry.
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);

    // ICONDIRENTRY: each side in one byte, the colour count, a spare byte, planes, depth, then the
    // payload's length and where it starts.
    data[6] = width;
    data[7] = height;
    data[8] = colourCount;
    data[9] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), bitCount);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)payload.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(18), _DirectorySize);

    payload.CopyTo(data, _DirectorySize);
    return data;
  }

  /// <summary>
  /// An 8 by 8 entry at four bits a pixel: a core header, sixteen three-byte colours, the rows
  /// bottom-up, and a mask that hides nothing.
  /// </summary>
  /// <remarks>
  /// Red, blue and green are the first three entries, and the picture is red but for a green pixel
  /// at the top left and a blue one at the bottom right — asymmetric both ways round, so a picture
  /// flipped or mirrored cannot pass.
  /// <para/>
  /// The depth deliberately sits where the old reader would not find it: offset 14 lands on the red
  /// byte of the first colour and offset 15 on the blue byte of the second, both 255, so reading
  /// the depth there yields 65535 rather than 4.
  /// </remarks>
  private static byte[] _CoreHeaderDib4Bpp() {
    const int width = 8;
    const int height = 8;
    const int colourStride = 4; // eight four-bit pixels, already a whole four-byte group
    const int maskStride = 4;

    var data = new byte[_CoreHeaderSize + 16 * 3 + colourStride * height + maskStride * height];

    // BITMAPCOREHEADER: its own length, then bcWidth and bcHeight as sixteen bits each, then the
    // plane and bit counts. The stated height covers the picture and the mask below it.
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), _CoreHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 4);

    // Sixteen RGBTRIPLEs of three bytes each, blue first. Entries three upwards stay black.
    var palette = _CoreHeaderSize;
    data[palette + 0] = 0; data[palette + 1] = 0; data[palette + 2] = 255;     // 0: red
    data[palette + 3] = 255; data[palette + 4] = 0; data[palette + 5] = 0;     // 1: blue
    data[palette + 6] = 0; data[palette + 7] = 255; data[palette + 8] = 0;     // 2: green

    // Rows bottom-up: the top row leads with index 2, the bottom row ends with index 1.
    var colours = palette + 16 * 3;
    for (var y = 0; y < height; ++y) {
      var row = colours + (height - 1 - y) * colourStride;
      if (y == 0)
        data[row] = 0x20;         // index 2 in the high nibble, the leftmost pixel
      else if (y == height - 1)
        data[row + 3] = 0x01;     // index 1 in the low nibble, the rightmost pixel
    }

    // The mask is left clear, so every pixel is drawn.
    return data;
  }

  /// <summary>
  /// An 8 by 8 entry at one bit a pixel, with the two-colour palette a core header spends six
  /// bytes on where a BITMAPINFOHEADER would spend eight.
  /// </summary>
  /// <remarks>
  /// The top row is white on its left half and the bottom row white on its right, so the picture
  /// cannot survive being flipped or mirrored. Windows reads this fixture as exactly that.
  /// </remarks>
  private static byte[] _CoreHeaderDib1Bpp() {
    const int height = 8;
    const int stride = 4;

    var data = new byte[_CoreHeaderSize + 2 * 3 + stride * height + stride * height];

    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), _CoreHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 1);

    // Black then white, three bytes an entry rather than four.
    data[_CoreHeaderSize + 3] = data[_CoreHeaderSize + 4] = data[_CoreHeaderSize + 5] = 255;

    var colours = _CoreHeaderSize + 2 * 3;
    for (var y = 0; y < height; ++y) {
      var row = colours + (height - 1 - y) * stride;
      if (y == 0)
        data[row] = 0xF0;               // the top row's left half
      else if (y == height - 1)
        data[row] = 0x0F;               // the bottom row's right half
    }

    return data;
  }

  // ── What the directory walk reports ──────────────────────────────────────

  [Test]
  [Category("Unit")]
  public void ReadBundle_TakesTheSixteenBitSizesFromACoreHeader() {
    var icon = _OneEntryIcon(8, 8, 16, 4, _CoreHeaderDib4Bpp());

    var entry = IcoReader.ReadBundle(icon).Entries[0];

    Assert.Multiple(() => {
      // Read at BITMAPINFOHEADER offsets the width and height are taken together as one number:
      // 8 | (16 << 16) is 1048584, and the height that follows becomes 131072.
      Assert.That(entry.Width, Is.EqualTo(8), "bcWidth and bcHeight must not be read as one 32-bit field");
      Assert.That(entry.Height, Is.EqualTo(8), "the stated height covers the mask and is halved");
      // Offset 14 is the first palette entry's red byte in a core header, not the depth.
      Assert.That(entry.BitsPerPixel, Is.EqualTo(4), "the depth of a core header is at offset 10");
      Assert.That(entry.Format, Is.EqualTo(IcoImageFormat.Bmp));
    });
  }

  [Test]
  [Category("Boundary")]
  public void ReadBundle_ReadsACoreHeaderAtTheLowestDepth() {
    var icon = _OneEntryIcon(8, 8, 2, 1, _CoreHeaderDib1Bpp());

    var entry = IcoReader.ReadBundle(icon).Entries[0];

    Assert.Multiple(() => {
      Assert.That(entry.Width, Is.EqualTo(8));
      Assert.That(entry.Height, Is.EqualTo(8));
      Assert.That(entry.BitsPerPixel, Is.EqualTo(1));
    });
  }

  // ── What the decoder makes of it ─────────────────────────────────────────

  [Test]
  [Category("Integration")]
  public void Decoded_ReadsACoreHeaderEntryAtItsOwnOffsets() {
    var image = IcoFile.ToRawImage(IcoReader.FromBytes(_OneEntryIcon(8, 8, 16, 4, _CoreHeaderDib4Bpp())));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed4));
      Assert.That(image.Width, Is.EqualTo(8));
      Assert.That(image.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_SpendsThreeBytesAPaletteEntryAndNotFour() {
    var image = IcoFile.ToRawImage(IcoReader.FromBytes(_OneEntryIcon(8, 8, 16, 4, _CoreHeaderDib4Bpp())));

    Assert.That(image.Palette, Is.Not.Null);
    var palette = image.Palette!;

    // At four bytes an entry the first colour still comes out right and every later one drifts:
    // the second would be read from the spare byte of the first and the start of the second, which
    // here is black rather than blue.
    Assert.Multiple(() => {
      Assert.That(new[] { palette[0], palette[1], palette[2] }, Is.EqualTo(new byte[] { 255, 0, 0 }), "the first colour is red");
      Assert.That(new[] { palette[3], palette[4], palette[5] }, Is.EqualTo(new byte[] { 0, 0, 255 }), "the second colour is blue");
      Assert.That(new[] { palette[6], palette[7], palette[8] }, Is.EqualTo(new byte[] { 0, 255, 0 }), "the third colour is green");
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_TurnsIntoThePixelsWindowsDraws() {
    var rgb = IcoFile.ToRawImage(IcoReader.FromBytes(_OneEntryIcon(8, 8, 16, 4, _CoreHeaderDib4Bpp()))).ToRgb24();

    static (byte R, byte G, byte B) At(byte[] pixels, int x, int y) {
      var i = (y * 8 + x) * 3;
      return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    Assert.Multiple(() => {
      Assert.That(At(rgb, 0, 0), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "green at the top left");
      Assert.That(At(rgb, 7, 7), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "blue at the bottom right");

      for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x) {
          if ((x == 0 && y == 0) || (x == 7 && y == 7))
            continue;

          Assert.That(At(rgb, x, y), Is.EqualTo(((byte)255, (byte)0, (byte)0)), $"red at {x}, {y}");
        }
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_ReadsATwoColourCoreHeaderEntry() {
    var rgb = IcoFile.ToRawImage(IcoReader.FromBytes(_OneEntryIcon(8, 8, 2, 1, _CoreHeaderDib1Bpp()))).ToRgb24();

    static byte Red(byte[] pixels, int x, int y) => pixels[(y * 8 + x) * 3];

    // The two-colour palette is six bytes here and eight in a BITMAPINFOHEADER, so reading it at
    // four bytes an entry starts the rows two bytes late and the picture comes apart.
    Assert.Multiple(() => {
      for (var x = 0; x < 4; ++x)
        Assert.That(Red(rgb, x, 0), Is.EqualTo(255), $"white at {x}, 0");
      for (var x = 4; x < 8; ++x)
        Assert.That(Red(rgb, x, 0), Is.EqualTo(0), $"black at {x}, 0");

      for (var x = 0; x < 4; ++x)
        Assert.That(Red(rgb, x, 7), Is.EqualTo(0), $"black at {x}, 7");
      for (var x = 4; x < 8; ++x)
        Assert.That(Red(rgb, x, 7), Is.EqualTo(255), $"white at {x}, 7");
    });
  }

  // ── The palette stride where an entry is turned back into a BMP ──────────

  [Test]
  [Category("Unit")]
  public void IconDibToBmpFile_PointsPastAThreeByteCorePalette() {
    var dib = _CoreHeaderDib4Bpp();

    var bmp = IcoPayload.IconDibToBmpFile(dib, 8, 8, 4);

    // Fourteen bytes of file header, twelve of core header, then sixteen colours of three bytes.
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)), Is.EqualTo((uint)(14 + 12 + 16 * 3)));
  }
}
