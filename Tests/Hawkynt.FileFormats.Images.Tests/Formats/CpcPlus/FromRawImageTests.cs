using System;
using FileFormat.Core;

namespace FileFormat.CpcPlus.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Four colours whose channels are multiples of seventeen, which four bits hold exactly.</summary>
  private static readonly int[] _Palette = [0x000000, 0x3366CC, 0xFF9900, 0xFFFFFF];

  private static RawImage _PaletteImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = _Palette[(x + y) & 3];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourColoursOnTheTwelveBitGrid_IsExact() {
    var source = _PaletteImage(CpcPlusFile.PixelWidth, CpcPlusFile.PixelHeight);

    var bytes = CpcPlusWriter.ToBytes(_Encode<CpcPlusFile>(source));
    var decoded = CpcPlusFile.ToRawImage(CpcPlusReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<CpcPlusFile>(_PaletteImage(64, 480));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(CpcPlusFile.PixelWidth));
      Assert.That(file.Height, Is.EqualTo(CpcPlusFile.PixelHeight));
      Assert.That(file.PixelData, Has.Length.EqualTo(CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow));
      Assert.That(file.PaletteData, Has.Length.EqualTo(CpcPlusFile.PaletteDataSize));
    });
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
