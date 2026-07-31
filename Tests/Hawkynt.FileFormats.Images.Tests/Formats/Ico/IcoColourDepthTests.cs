using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Ico;
using NUnit.Framework;

namespace Hawkynt.FileFormats.Images.Tests.Formats.Ico;

/// <summary>
/// Every colour depth an ICO has ever been written at, decoded and checked pixel by pixel.
/// </summary>
/// <remarks>
/// <para>
/// The icon format spans thirty years: Windows 3.0 wrote 1-bit icons, 3.1 and 95 wrote 4- and 8-bit
/// ones, XP brought 32-bit alpha, and Vista onwards embeds PNG for the 256-pixel sizes. Only 32, 24,
/// 8 and 4 were implemented, so a monochrome icon — the oldest and simplest kind there is — threw
/// <c>NotSupportedException</c>. That is also what a modern tool emits for a two-colour image, so
/// this was not only a retro-compatibility gap.
/// </para>
/// <para>
/// Each case is built here rather than loaded from a fixture, so the expected pixels are stated
/// rather than assumed, and the suite needs no icon files checked in beside it.
/// </para>
/// </remarks>
[TestFixture]
public class IcoColourDepthTests {

  private const uint _Red = 0xFFFF0000;
  private const uint _Green = 0xFF00FF00;
  private const uint _Blue = 0xFF0000FF;

  /// <summary>
  /// A 2x2 icon at each indexed or packed depth: red, green, blue, and one pixel the mask hides.
  /// </summary>
  [TestCase(1)]
  [TestCase(4)]
  [TestCase(8)]
  [TestCase(16)]
  [TestCase(24)]
  [TestCase(32)]
  public void Every_Colour_Depth_Decodes_To_The_Colours_It_Was_Given(int bitCount) {
    var ico = _BuildIcon(bitCount);

    var raw = IcoFile.ToRawImage(IcoReader.FromBytes(ico), 0);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(2), "width");
      Assert.That(raw.Height, Is.EqualTo(2), "height");
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Bgra32), "an icon carries transparency, so it decodes to BGRA");
      Assert.That(_At(raw, 0, 0), Is.EqualTo(_Red), "top-left");
      Assert.That(_At(raw, 1, 0), Is.EqualTo(_Green), "top-right");
      // Two palette slots is all a monochrome icon has, so the third colour is only asked of the
      // depths that can hold one.
      if (bitCount > 1)
        Assert.That(_At(raw, 0, 1), Is.EqualTo(_Blue), "bottom-left");

      Assert.That(_At(raw, 1, 1) >> 24, Is.EqualTo(0), "the hidden pixel is transparent");
    });
  }

  /// <summary>
  /// A 1-bit icon is the Windows 3.0 case, and the one that used to throw outright.
  /// </summary>
  [Test]
  public void A_Monochrome_Icon_Is_Not_Rejected() {
    var ico = _BuildIcon(1);

    Assert.That(() => IcoFile.ToRawImage(IcoReader.FromBytes(ico), 0), Throws.Nothing);
  }

  /// <summary>
  /// A 32-bit icon whose alpha channel was left at zero is opaque, not invisible.
  /// </summary>
  /// <remarks>
  /// Icons written before XP settled the convention fill BGRA and never touch the alpha byte. Read
  /// literally that is a wholly invisible icon, so the AND mask is believed instead — which is what
  /// Windows itself does with them.
  /// </remarks>
  [Test]
  public void A_32_Bit_Icon_With_No_Alpha_Falls_Back_To_Its_Mask() {
    var ico = _BuildIcon(32, zeroAlpha: true);

    var raw = IcoFile.ToRawImage(IcoReader.FromBytes(ico), 0);

    Assert.Multiple(() => {
      Assert.That(_At(raw, 0, 0) >> 24, Is.EqualTo(255), "an unmasked pixel stays visible");
      Assert.That(_At(raw, 1, 1) >> 24, Is.EqualTo(0), "the masked pixel is still transparent");
    });
  }

  /// <summary>
  /// The transparency of every sub-32-bit depth comes from the AND mask and nowhere else.
  /// </summary>
  [TestCase(1)]
  [TestCase(4)]
  [TestCase(8)]
  [TestCase(24)]
  public void The_And_Mask_Is_What_Makes_A_Classic_Icon_Transparent(int bitCount) {
    var raw = IcoFile.ToRawImage(IcoReader.FromBytes(_BuildIcon(bitCount)), 0);

    Assert.Multiple(() => {
      Assert.That(_At(raw, 1, 1) >> 24, Is.EqualTo(0), "the masked pixel");
      Assert.That(_At(raw, 0, 0) >> 24, Is.EqualTo(255), "an unmasked pixel");
    });
  }

  /// <summary>A truncated entry is a bad file, not an unhandled index-out-of-range.</summary>
  [Test]
  public void A_Truncated_Entry_Is_Reported_As_Bad_Data() {
    var ico = _BuildIcon(8);
    Array.Resize(ref ico, ico.Length - 24);
    // The directory still claims the original length, which is exactly how a half-copied file looks.

    Assert.That(
      () => IcoFile.ToRawImage(IcoReader.FromBytes(ico), 0),
      Throws.InstanceOf<InvalidDataException>());
  }

  private static uint _At(RawImage raw, int x, int y) {
    var at = (((y * raw.Width) + x) * 4);
    var pixels = raw.PixelData;
    return ((uint)pixels[at + 3] << 24) | ((uint)pixels[at + 2] << 16) | ((uint)pixels[at + 1] << 8) | pixels[at];
  }

  /// <summary>
  /// A 2x2 single-entry .ico at the given depth: red, green, blue and one masked pixel.
  /// </summary>
  private static byte[] _BuildIcon(int bitCount, bool zeroAlpha = false) {
    const int width = 2;
    const int height = 2;

    var paletteEntries = bitCount <= 8 ? 1 << bitCount : 0;
    var colourStride = ((width * bitCount) + 31) / 32 * 4;
    var maskStride = (width + 31) / 32 * 4;
    var dib = new byte[40 + (paletteEntries * 4) + (colourStride * height) + (maskStride * height)];

    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2); // colour bitmap plus mask
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), (ushort)bitCount);

    // Palette: 0 red, 1 green, 2 blue, and whatever else the depth has room for.
    var palette = new (byte R, byte G, byte B)[] { (255, 0, 0), (0, 255, 0), (0, 0, 255) };
    for (var i = 0; i < paletteEntries && i < palette.Length; ++i) {
      var at = 40 + (i * 4);
      dib[at + 0] = palette[i].B;
      dib[at + 1] = palette[i].G;
      dib[at + 2] = palette[i].R;
    }

    // Rows are stored bottom-up, so y=1 is written first.
    var pixelsAt = 40 + (paletteEntries * 4);
    var third = bitCount > 1 ? 2 : 0;
    var wanted = new (int X, int Y, int Index)[] { (0, 0, 0), (1, 0, 1), (0, 1, third), (1, 1, 0) };
    foreach (var (x, y, index) in wanted) {
      var row = pixelsAt + ((height - 1 - y) * colourStride);
      switch (bitCount) {
        case 1:
        case 4: {
          var perByte = 8 / bitCount;
          var shift = (perByte - 1 - (x % perByte)) * bitCount;
          dib[row + (x / perByte)] |= (byte)((index & ((1 << bitCount) - 1)) << shift);
          break;
        }
        case 8:
          dib[row + x] = (byte)index;
          break;
        case 16: {
          var (r, g, b) = (palette[index].R, palette[index].G, palette[index].B);
          var value = (ushort)(((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3));
          BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(row + (x * 2)), value);
          break;
        }
        case 24: {
          var (r, g, b) = (palette[index].R, palette[index].G, palette[index].B);
          dib[row + (x * 3) + 0] = b;
          dib[row + (x * 3) + 1] = g;
          dib[row + (x * 3) + 2] = r;
          break;
        }
        default: {
          var (r, g, b) = (palette[index].R, palette[index].G, palette[index].B);
          dib[row + (x * 4) + 0] = b;
          dib[row + (x * 4) + 1] = g;
          dib[row + (x * 4) + 2] = r;
          // At 32 bits the alpha channel is what says transparent, and Windows lets it overrule the
          // mask — so the hidden pixel states it there, exactly as a real 32-bit icon does.
          var hidden = x == 1 && y == 1;
          dib[row + (x * 4) + 3] = zeroAlpha || hidden ? (byte)0 : (byte)255;
          break;
        }
      }
    }

    // The AND mask hides the bottom-right pixel; a set bit means "show the desktop through".
    var maskAt = pixelsAt + (colourStride * height);
    const int maskedX = 1, maskedY = 1;
    var maskRow = maskAt + ((height - 1 - maskedY) * maskStride);
    dib[maskRow + (maskedX >> 3)] |= (byte)(1 << (7 - (maskedX & 7)));

    // A 32-bit icon whose alpha is deliberately zero must still show its unmasked pixels, so the
    // mask is what says which those are — the same mask every other depth uses.
    var ico = new byte[6 + 16 + dib.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1); // an icon, not a cursor
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 1); // one entry
    ico[6] = width;
    ico[7] = height;
    ico[8] = (byte)(paletteEntries <= 255 ? paletteEntries : 0);
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(12), (ushort)bitCount);
    BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(14), dib.Length);
    BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(18), 22);
    dib.CopyTo(ico.AsSpan(22));
    return ico;
  }
}
