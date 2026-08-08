using System;
using FileFormat.Core;

namespace FileFormat.HiresManager.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Every 8x8 cell holds exactly two of the machine's own colours, which is the most a
  /// hires cell can show — so nothing has to be approximated and the round trip is exact.</summary>
  private static RawImage _TwoColorCells() {
    const int width = HiresManagerFile.FixedWidth, height = HiresManagerFile.FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = (y / 8) * (width / 8) + x / 8;
      var color = Commodore64Graphics.HexColors[(x + y) % 2 == 0 ? cell % 16 : (cell + 5) % 16];
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColorCells_ReproducesExactly() {
    var source = _TwoColorCells();
    var file = HiresManagerFile.FromRawImage(source);
    var restored = HiresManagerReader.FromBytes(HiresManagerWriter.ToBytes(file));
    var decoded = HiresManagerFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // This screen has one size and no other, so a picture of a different size is brought to it
    // rather than refused.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = HiresManagerFile.ToRawImage(HiresManagerFile.FromRawImage(Raw(100, 100)));
    var large = HiresManagerFile.ToRawImage(HiresManagerFile.FromRawImage(Raw(640, 480)));

    Assert.That((small.Width, small.Height), Is.EqualTo((large.Width, large.Height)));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesTheBitmapThenTheVideoMatrixBehindTheStandardLoadAddress() {
    var file = HiresManagerFile.FromRawImage(_TwoColorCells());

    Assert.Multiple(() => {
      Assert.That(file.LoadAddress, Is.EqualTo(0x4000));
      Assert.That(file.RawData, Has.Length.EqualTo(9000));
    });
  }
}
