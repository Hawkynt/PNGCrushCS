using System;
using FileFormat.Core;

namespace FileFormat.MicroIllustratorA8.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The four mode E registers the decoder re-applies.</summary>
  private static readonly int[] _Palette = [0x000000, 0x884400, 0x00AA44, 0xDDCC88];

  private static RawImage _PaletteImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = _Palette[(x * 3 + y) & 3];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourModeEColours_IsExact() {
    var source = _PaletteImage(160, 192);

    var bytes = MicroIllustratorA8Writer.ToBytes(_Encode<MicroIllustratorA8File>(source));
    var decoded = MicroIllustratorA8File.ToRawImage(MicroIllustratorA8Reader.FromBytes(bytes));

    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 160; ++x)
      Assert.That(decoded.PixelData[y * 160 + x], Is.EqualTo((x * 3 + y) & 3), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<MicroIllustratorA8File>(_PaletteImage(48, 300));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(160));
      Assert.That(file.Height, Is.EqualTo(192));
      Assert.That(file.PixelData, Has.Length.EqualTo(MicroIllustratorA8File.ExpectedFileSize));
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
