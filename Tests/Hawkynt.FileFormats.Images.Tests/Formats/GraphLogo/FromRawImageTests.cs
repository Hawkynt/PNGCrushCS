using System;
using FileFormat.Core;

namespace FileFormat.GraphLogo.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Four of the machine's colours, which is what a cell can draw without its inverse bit.</summary>
  private static readonly byte[] _Registers = [0x00, 0x28, 0x86, 0x0E];

  /// <summary>
  /// A picture in four register colours, each held for two columns — a mode 4 pixel is two screen
  /// pixels wide, so only a picture already doubled can come back unchanged.
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
  public void RoundTrip_FourRegisterColours_ReproducesExactly() {
    var source = _Doubled(GraphLogoFile.Width, GraphLogoFile.Height);
    var file = GraphLogoFile.FromRawImage(source);
    var decoded = GraphLogoFile.ToRawImage(GraphLogoReader.FromBytes(GraphLogoWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((GraphLogoFile.Width, GraphLogoFile.Height)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // The screen is one size and no other, so a picture of another is brought to it.
    var file = GraphLogoFile.FromRawImage(_Doubled(37, 11));

    Assert.That(
      file.Data,
      Has.Length.EqualTo(GraphLogoFile.FontOffset + GraphLogoFile.CharacterRows * GraphLogoFile.FontSize
                         + GraphLogoFile.TrailerSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_GivesEveryRowItsOwnCharacterSet() {
    // A row holds forty cells and a set holds 128 glyphs, so every cell can have a glyph of its own
    // and nothing is shared — which is what redefining the set between rows buys.
    var file = GraphLogoFile.FromRawImage(_Doubled(GraphLogoFile.Width, GraphLogoFile.Height));

    Assert.Multiple(() => {
      for (var row = 0; row < GraphLogoFile.CharacterRows; ++row)
        Assert.That(file.Data[row], Is.EqualTo(row), $"row {row}");
    });
  }
}
