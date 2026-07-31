using System;
using System.Buffers.Binary;
using FileFormat.Bmp;
using NUnit.Framework;

namespace Hawkynt.FileFormats.Images.Tests.Formats.Bmp;

/// <summary>
/// The 12-byte BITMAPCOREHEADER, which OS/2 1.x and Windows 2.0 wrote and every tool still emits on
/// request.
/// </summary>
/// <remarks>
/// The reader rejected any header shorter than the 40-byte BITMAPINFOHEADER, so these were refused
/// outright — the same gap the icon decoder had, and for the same reason: the older, simpler header
/// was treated as a malformed version of the newer one. It differs in two ways that matter, 16-bit
/// dimensions and three-byte palette entries, so a reader that merely stopped rejecting it would
/// still have read the palette at the wrong stride.
/// </remarks>
[TestFixture]
public class BitmapCoreHeaderTests {

  [Test]
  public void An_Os2_Core_Header_Bitmap_Is_Read() {
    var bmp = _BuildCoreHeaderBitmap();

    var file = BmpReader.FromBytes(bmp);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2), "width comes from a 16-bit field here");
      Assert.That(file.Height, Is.EqualTo(2), "height comes from a 16-bit field here");
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    });
  }

  /// <summary>
  /// The palette is read at three bytes an entry, not four.
  /// </summary>
  /// <remarks>
  /// The distinguishing test: a reader that accepted the short header but kept the four-byte stride
  /// would walk off the end of a three-colour palette and hand back the wrong colours entirely.
  /// </remarks>
  [Test]
  public void The_Palette_Is_Read_At_Three_Bytes_An_Entry() {
    var file = BmpReader.FromBytes(_BuildCoreHeaderBitmap());

    Assert.Multiple(() => {
      Assert.That(file.Palette, Is.Not.Null);
      Assert.That(file.Palette![0], Is.EqualTo(255), "entry 0 red");
      Assert.That(file.Palette[1], Is.EqualTo(0), "entry 0 green");
      Assert.That(file.Palette[2], Is.EqualTo(0), "entry 0 blue");
      Assert.That(file.Palette[3], Is.EqualTo(0), "entry 1 red");
      Assert.That(file.Palette[4], Is.EqualTo(255), "entry 1 green");
      Assert.That(file.Palette[5], Is.EqualTo(0), "entry 1 blue");
    });
  }

  /// <summary>A 2x2, 8-bit, four-colour bitmap behind a BITMAPCOREHEADER.</summary>
  private static byte[] _BuildCoreHeaderBitmap() {
    const int width = 2;
    const int height = 2;
    const int palette = 256;

    var stride = (width + 3) & ~3;
    var pixelsAt = 14 + 12 + (palette * 3);
    var bmp = new byte[pixelsAt + (stride * height)];

    bmp[0] = (byte)'B';
    bmp[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), pixelsAt);

    // BITMAPCOREHEADER: size, then 16-bit width, height, planes and bit count.
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 12);
    BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(18), width);
    BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(20), height);
    BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(22), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(24), 8);

    // Three-byte BGR entries: red, green, blue.
    var colours = new (byte R, byte G, byte B)[] { (255, 0, 0), (0, 255, 0), (0, 0, 255) };
    for (var i = 0; i < colours.Length; ++i) {
      var at = 26 + (i * 3);
      bmp[at + 0] = colours[i].B;
      bmp[at + 1] = colours[i].G;
      bmp[at + 2] = colours[i].R;
    }

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        bmp[pixelsAt + (y * stride) + x] = (byte)((y * width) + x);

    return bmp;
  }
}
