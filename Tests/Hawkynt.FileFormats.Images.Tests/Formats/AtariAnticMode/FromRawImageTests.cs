using System;
using FileFormat.Core;

namespace FileFormat.AtariAnticMode.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The four mode E registers the decoder re-applies.</summary>
  private static readonly int[] _ModeE = [0x000000, 0x884400, 0x00AA44, 0xDDCC88];

  private static RawImage _ModeEImage(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = _ModeE[(x + y * 2) & 3];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static RawImage _BlackAndWhite(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      if (((x * 3 + y) & 3) == 0) {
        var offset = (y * width + x) * 3;
        rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = 255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FourModeEColours_IsExact() {
    var source = _ModeEImage(160, 192);

    var file = _Encode<AtariAnticModeFile>(source);
    var decoded = AtariAnticModeFile.ToRawImage(AtariAnticModeReader.FromBytes(AtariAnticModeWriter.ToBytes(file)));

    Assert.That(file.Mode, Is.EqualTo(AtariAnticModeFile.ModeE));
    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 160; ++x)
      Assert.That(decoded.PixelData[y * 160 + x], Is.EqualTo((x + y * 2) & 3), $"pixel {x},{y}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ABlackAndWhitePicture_TakesModeFAndIsExact() {
    var source = _BlackAndWhite(320, 192);

    var file = _Encode<AtariAnticModeFile>(source);
    var decoded = AtariAnticModeFile.ToRawImage(AtariAnticModeReader.FromBytes(AtariAnticModeWriter.ToBytes(file)));

    Assert.That(file.Mode, Is.EqualTo(AtariAnticModeFile.ModeF));
    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 320; ++x) {
      var bit = (decoded.PixelData[y * 40 + x / 8] >> (7 - x % 8)) & 1;
      Assert.That(bit, Is.EqualTo(((x * 3 + y) & 3) == 0 ? 1 : 0), $"pixel {x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<AtariAnticModeFile>(_ModeEImage(64, 64));

    Assert.Multiple(() => {
      Assert.That(file.PixelData, Has.Length.EqualTo(AtariAnticModeFile.ScreenDataSize));
      Assert.That(file.Width, Is.EqualTo(160));
      Assert.That(file.Height, Is.EqualTo(192));
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
