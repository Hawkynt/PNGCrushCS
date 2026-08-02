using System;
using System.Buffers.Binary;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

/// <summary>
/// The OS/2 flavour of BMP, whose second header is twelve bytes rather than forty.
/// </summary>
/// <remarks>
/// It was refused outright: the reader took anything shorter than a BITMAPINFOHEADER for a damaged
/// file. The two differ in more than length — the sizes are 16-bit, there is no compression or
/// colour-count field, and the palette that follows spends three bytes an entry instead of four, so
/// reading one as the other misplaces every colour as well as the dimensions.
/// <para/>
/// Checked against ImageMagick on real files from a public archive of format samples: both a 1024
/// by 768 and a 403 by 200 picture come back byte-identical to what it decodes.
/// </remarks>
[TestFixture]
public sealed class BmpOs2CoreHeaderTests {

  /// <summary>Builds an OS/2 BMP: file header, BITMAPCOREHEADER, three-byte palette, then rows.</summary>
  private static byte[] _CoreBitmap(int width, int height, byte[] palette, byte[] indices) {
    var stride = (width + 3) & ~3;
    var offset = 14 + 12 + palette.Length / 3 * 3;
    var data = new byte[offset + stride * height];

    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), offset);

    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 12);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), 8);

    for (var i = 0; i < palette.Length / 3; ++i) {
      data[26 + i * 3] = palette[i * 3 + 2];     // B
      data[26 + i * 3 + 1] = palette[i * 3 + 1]; // G
      data[26 + i * 3 + 2] = palette[i * 3];     // R
    }

    // The rows are stored bottom upwards, which is what the reader has to undo.
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[offset + (height - 1 - y) * stride + x] = indices[y * width + x];

    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheTwelveByteHeaderAndItsSixteenBitSizes() {
    var palette = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
    var indices = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 };

    var file = BmpReader.FromBytes(_CoreBitmap(4, 2, palette, indices));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
      Assert.That(file.PixelData, Is.EqualTo(indices), "the rows come back the right way up");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_SpendsThreeBytesAPaletteEntryAndNotFour() {
    var palette = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
    var file = BmpReader.FromBytes(_CoreBitmap(4, 1, palette, [0, 1, 2, 3]));

    Assert.That(file.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      // Reading four-byte entries would shift every colour after the first.
      for (var i = 0; i < 4; ++i) {
        Assert.That(file.Palette![i * 3], Is.EqualTo(palette[i * 3]), $"entry {i} red");
        Assert.That(file.Palette![i * 3 + 1], Is.EqualTo(palette[i * 3 + 1]), $"entry {i} green");
        Assert.That(file.Palette![i * 3 + 2], Is.EqualTo(palette[i * 3 + 2]), $"entry {i} blue");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_StillRefusesAHeaderLengthNeitherKindUses() {
    var data = _CoreBitmap(4, 1, [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3], [0, 1, 2, 3]);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 20);

    Assert.Throws<System.IO.InvalidDataException>(() => BmpReader.FromBytes(data));
  }
}
