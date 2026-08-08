using System;
using FileFormat.Core;

namespace FileFormat.CpcAdvanced.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The firmware palette the decoder re-applies, so a picture built from it survives intact.</summary>
  private static readonly int[] _Palette = [
    0x000000, 0x000080, 0x0000FF, 0x800000, 0x800080, 0x8000FF, 0x808000, 0x808080,
    0x8080FF, 0xFF0000, 0xFF0080, 0xFF00FF, 0xFF8000, 0xFF8080, 0xFF80FF, 0xFFFF00,
  ];

  private static RawImage _PaletteImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = _Palette[(x + y * 3) & 15];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_APictureOnTheFirmwarePalette_IsExact() {
    var source = _PaletteImage(CpcAdvancedFile.PixelWidth, CpcAdvancedFile.PixelHeight);

    var bytes = CpcAdvancedWriter.ToBytes(_Encode<CpcAdvancedFile>(source));
    var decoded = CpcAdvancedFile.ToRawImage(CpcAdvancedReader.FromBytes(bytes));

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
    for (var y = 0; y < CpcAdvancedFile.PixelHeight; ++y)
    for (var x = 0; x < CpcAdvancedFile.PixelWidth; ++x)
      Assert.That(decoded.PixelData[y * CpcAdvancedFile.PixelWidth + x], Is.EqualTo((x + y * 3) & 15), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<CpcAdvancedFile>(_PaletteImage(320, 100));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(CpcAdvancedFile.PixelWidth));
      Assert.That(file.Height, Is.EqualTo(CpcAdvancedFile.PixelHeight));
      Assert.That(file.PixelData, Has.Length.EqualTo(CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow));
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
