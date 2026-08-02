using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.MayaIff.Tests;

/// <summary>
/// Maya IFF: a form holding a version, a header, and a second form holding the tiles.
/// </summary>
/// <remarks>
/// The picture is stored as 64-pixel tiles, and each states its own corners before its pixels. What
/// was written here before was the header and then one chunk of the whole picture — no version, no
/// nested form, no corners — so a reader took the first four samples as a tile's corners and went
/// looking for memory for a tile 65535 square.
/// <para/>
/// The structure below was taken from a file Maya itself wrote. What a tile holds between its
/// corners and its end is not settled: a reader shown ours draws one plane of it in grey. That is
/// recorded on the writer rather than asserted here, because these tests should say what is known.
/// </remarks>
[TestFixture]
public sealed class MayaIffTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static int _Find(byte[] data, string tag) {
    var wanted = Encoding.ASCII.GetBytes(tag);
    for (var i = 0; i + wanted.Length <= data.Length; ++i) {
      var match = true;
      for (var j = 0; j < wanted.Length; ++j)
        if (data[i + j] != wanted[j]) {
          match = false;
          break;
        }

      if (match)
        return i;
    }

    return -1;
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheFormsAndChunksTheFormatUses() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("FOR4"));
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("CIMG"));
      Assert.That(_Find(bytes, "FVER"), Is.GreaterThan(0), "the version chunk");
      Assert.That(_Find(bytes, "TBHD"), Is.GreaterThan(0), "the header");
      Assert.That(_Find(bytes, "TBMP"), Is.GreaterThan(0), "the nested form the tiles live in");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_CutsThePictureIntoTilesAndNamesTheirCorners() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(320, 200)));
    var tbhd = _Find(bytes, "TBHD");

    // Five tiles across and four down for a 320 by 200 picture at sixty-four a side.
    // Within the header: width and height take four each, the pixel ratio two each, the flags four,
    // then the byte depth and the tile count two each.
    var tiles = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tbhd + 8 + 18));
    Assert.That(tiles, Is.EqualTo(5 * 4));

    var first = _Find(bytes, "TBMP") + 4;
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 8)), Is.EqualTo(0), "left");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 10)), Is.EqualTo(0), "top");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 12)), Is.EqualTo(63), "right");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 14)), Is.EqualTo(63), "bottom");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_SaysOneByteAChannel() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(64, 64)));
    var tbhd = _Find(bytes, "TBHD");

    // Saying two would send a reader looking for twice the data there is.
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tbhd + 8 + 16)), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PutsEveryTileBackWhereItCameFrom() {
    // A picture wider than one tile is the case that catches a reader laying tiles out in the order
    // it meets them rather than at the corners each one names.
    var original = _Picture(200, 100);
    var restored = MayaIffReader.FromBytes(MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(original)));

    Assert.That(restored.Width, Is.EqualTo(200));
    Assert.That(restored.Height, Is.EqualTo(100));

    var image = MayaIffFile.ToRawImage(restored);
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    Assert.That(rgb.PixelData, Is.EqualTo(original.PixelData));
  }
}
