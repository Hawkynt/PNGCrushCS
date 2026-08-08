using System;
using FileFormat.Core;

namespace FileFormat.HardColorMap.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Four of the machine's colours, which is what the playfield alone draws.</summary>
  private static readonly byte[] _Registers = [0x00, 0x28, 0x86, 0x0E];

  /// <summary>
  /// A picture in those four, each held for two columns — a playfield pixel is two screen pixels
  /// wide, so only a picture already doubled can come back unchanged.
  /// </summary>
  private static RawImage _Doubled(int width, int height) {
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = _Registers[((x >> 1) + y * 3) & 3] * 3;
      var at = (y * width + x) * 3;
      data[at] = gtia[entry];
      data[at + 1] = gtia[entry + 1];
      data[at + 2] = gtia[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourPlayfieldColours_ReproducesExactly() {
    var source = _Doubled(HardColorMapFile.Width, HardColorMapFile.Height);
    var file = HardColorMapFile.FromRawImage(source);
    var decoded = HardColorMapFile.ToRawImage(HardColorMapReader.FromBytes(HardColorMapWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(
        (decoded.Width, decoded.Height), Is.EqualTo((HardColorMapFile.Width, HardColorMapFile.Height)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // The screen is one size and no other, so a picture of another is brought to it.
    var file = HardColorMapFile.FromRawImage(_Doubled(37, 11));

    Assert.That(file.Data, Has.Length.EqualTo(HardColorMapFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_LeavesEverySpriteEmpty() {
    // The colour map the name refers to is eight objects repositioned twice per scanline, and what a
    // pixel then shows depends on which of them cover it. Nothing here tries to choose them, so the
    // area they would live in stays clear and the playfield alone is what the picture is.
    var file = HardColorMapFile.FromRawImage(_Doubled(HardColorMapFile.Width, HardColorMapFile.Height));

    Assert.That(
      file.Data[HardColorMapFile.FirstPlayerOffset..HardColorMapFile.PlayfieldOffset], Is.All.Zero);
  }
}
