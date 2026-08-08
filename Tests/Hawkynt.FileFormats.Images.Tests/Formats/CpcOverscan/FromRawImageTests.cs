using System;
using FileFormat.Core;

namespace FileFormat.CpcOverscan.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The four mode 1 inks the decoder re-applies.</summary>
  private static readonly int[] _Palette = [0x000000, 0x0000FF, 0xFF0000, 0xFFFF00];

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
  public void RoundTrip_APictureOnTheModeOnePalette_IsExact() {
    var source = _PaletteImage(CpcOverscanFile.PixelWidth, CpcOverscanFile.PixelHeight);

    var bytes = CpcOverscanWriter.ToBytes(_Encode<CpcOverscanFile>(source));
    var decoded = CpcOverscanFile.ToRawImage(CpcOverscanReader.FromBytes(bytes));

    for (var y = 0; y < CpcOverscanFile.PixelHeight; ++y)
    for (var x = 0; x < CpcOverscanFile.PixelWidth; ++x)
      Assert.That(decoded.PixelData[y * CpcOverscanFile.PixelWidth + x], Is.EqualTo((x + y) & 3), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<CpcOverscanFile>(_PaletteImage(100, 100));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(CpcOverscanFile.PixelWidth));
      Assert.That(file.Height, Is.EqualTo(CpcOverscanFile.PixelHeight));
      Assert.That(file.PixelData, Has.Length.EqualTo(CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow));
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
