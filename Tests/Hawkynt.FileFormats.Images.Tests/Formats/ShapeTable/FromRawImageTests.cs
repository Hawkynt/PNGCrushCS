using System;
using FileFormat.Core;

namespace FileFormat.ShapeTable.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Every 4x8 cell is one flat colour of the machine's own sixteen, which any multicolour
  /// cell can hold — so nothing is approximated and the round trip is exact.</summary>
  private static RawImage _SolidCells() {
    const int width = 160, height = 200;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = (y / 8) * (width / 4) + x / 4;
      var color = Commodore64Graphics.HexColors[cell % 16];
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidCells_ReproducesExactly() {
    var source = _SolidCells();
    var file = ShapeTableFileType.FromRawImage(source);
    var restored = ShapeTableReader.FromBytes(ShapeTableWriter.ToBytes(file));

    Assert.That(restored.Kind, Is.EqualTo(ShapeTableKind.C64Multicolor));
    Assert.That(ShapeTableFileType.ToRawImage(restored).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NoisyScreen_ReproducesEveryByteOfTheScreen() {
    // The packing has to survive data with no runs in it at all, where every byte equal to the
    // chosen escape costs three and nothing else compresses.
    var random = new Random(20250808);
    var screen = new byte[ShapeTableFileType.MulticolorScreenSize];
    random.NextBytes(screen);

    var file = new ShapeTableFileType {
      Data = screen, Kind = ShapeTableKind.C64Multicolor, Width = 160, Height = 200
    };

    var restored = ShapeTableReader.FromBytes(ShapeTableWriter.ToBytes(file));

    Assert.That(restored.Kind, Is.EqualTo(ShapeTableKind.C64Multicolor));
    Assert.That(restored.Data[..ShapeTableFileType.MulticolorScreenSize], Is.EqualTo(screen));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LongRuns_ReproducesEveryByteOfTheScreen() {
    // The other end: a run of more than 256 has to become several, and a run ending exactly on a
    // section boundary must not carry into the next section under a different escape byte.
    var screen = new byte[ShapeTableFileType.MulticolorScreenSize];
    for (var i = 0; i < 8000; ++i)
      screen[i] = 0xAA;
    for (var i = 8000; i < 9000; ++i)
      screen[i] = 0;
    for (var i = 9000; i < 10000; ++i)
      screen[i] = 255;

    var file = new ShapeTableFileType {
      Data = screen, Kind = ShapeTableKind.C64Multicolor, Width = 160, Height = 200
    };

    var restored = ShapeTableReader.FromBytes(ShapeTableWriter.ToBytes(file));

    Assert.That(restored.Data[..ShapeTableFileType.MulticolorScreenSize], Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // The screen this writes has one size and no other, so a picture of another is brought to it.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = ShapeTableFileType.FromRawImage(Raw(40, 25));
    var large = ShapeTableFileType.FromRawImage(Raw(1024, 768));

    Assert.Multiple(() => {
      Assert.That((small.Width, small.Height), Is.EqualTo((160, 200)));
      Assert.That((large.Width, large.Height), Is.EqualTo((160, 200)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_KeepsTheThreeFormsThatWereReadOutWhole() {
    // Vectors, the Atari screen and the Loadstar screen are held as the file's own bytes, so
    // writing them back is a copy rather than a reassembly.
    var data = new byte[ShapeTableFileType.LoadstarFileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)i;

    var file = new ShapeTableFileType {
      Data = data, Kind = ShapeTableKind.Loadstar, Width = 160, Height = 200
    };

    Assert.That(ShapeTableWriter.ToBytes(file), Is.EqualTo(data));
  }
}
