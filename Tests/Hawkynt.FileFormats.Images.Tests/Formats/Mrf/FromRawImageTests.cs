using System;
using FileFormat.Core;

namespace FileFormat.Mrf.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture of two tones with a width that is neither a multiple of eight nor of a tile.</summary>
  private static RawImage _Checks(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)(((x / 3) + (y / 5)) % 2 == 0 ? 0 : 255);
        var at = (y * width + x) * 3;
        data[at] = data[at + 1] = data[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoTones_ReproducesEveryPixel() {
    var source = _Checks(101, 37);
    var file = MrfFile.FromRawImage(source);
    var decoded = MrfReader.FromBytes(MrfWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((101, 37)));
      Assert.That(decoded.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = MrfFile.FromRawImage(_Checks(200, 3));
    var tall = MrfFile.FromRawImage(_Checks(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ReducesAColourPictureByBrightness() {
    var colour = new RawImage {
      Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = [0, 0, 0, 255, 255, 255],
    };

    Assert.That(MrfFile.FromRawImage(colour).PixelData, Is.EqualTo(new byte[] { 0, 1 }));
  }

  /// <summary>
  /// The quadtree is what the format is: a square of one colour costs a bit saying so and a bit
  /// giving the colour, whatever its side. A whole tile of white is therefore two bits, and the
  /// smallest file is the header and the one byte they round up to.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_AUniformTileCostsTwoBits() {
    var white = new RawImage {
      Width = MrfFile.TileSize, Height = MrfFile.TileSize, Format = PixelFormat.Gray8,
      PixelData = new byte[MrfFile.TileSize * MrfFile.TileSize],
    };

    Array.Fill(white.PixelData, (byte)255);
    var bytes = MrfWriter.ToBytes(MrfFile.FromRawImage(white));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(MrfFile.HeaderSize + 1));
      Assert.That(bytes[MrfFile.HeaderSize], Is.EqualTo(0xC0), "one bit for uniform, one for white, and the rest of the byte unread");
    });
  }

  /// <summary>
  /// The squares are coded over a canvas rounded up to whole tiles, and the picture is its top-left
  /// corner. A writer that coded the stated size instead would put every row after the first at the
  /// wrong offset, and only a picture whose width is not a multiple of the tile shows it.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_AWidthThatIsNotAWholeTile_KeepsItsRowsAligned() {
    var source = _Checks(MrfFile.TileSize + 5, MrfFile.TileSize + 7);
    var file = MrfFile.FromRawImage(source);
    var decoded = MrfReader.FromBytes(MrfWriter.ToBytes(file));

    Assert.That(decoded.PixelData, Is.EqualTo(file.PixelData));
  }
}
