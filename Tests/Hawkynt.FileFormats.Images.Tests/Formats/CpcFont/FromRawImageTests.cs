using System;
using FileFormat.Core;

namespace FileFormat.CpcFont.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Sheet(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var lit = ((x * 5 + y * 3) & 7) < 3;
      var offset = (y * width + x) * 3;
      rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = lit ? (byte)255 : (byte)0;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ATwoColourSheet_IsExact() {
    var source = _Sheet(CpcFontFile.PixelWidth, CpcFontFile.PixelHeight);

    var bytes = CpcFontWriter.ToBytes(_Encode<CpcFontFile>(source));
    var decoded = CpcFontFile.ToRawImage(CpcFontReader.FromBytes(bytes));

    var stride = CpcFontFile.PixelWidth / 8;
    for (var y = 0; y < CpcFontFile.PixelHeight; ++y)
    for (var x = 0; x < CpcFontFile.PixelWidth; ++x) {
      var bit = (decoded.PixelData[y * stride + x / 8] >> (7 - x % 8)) & 1;
      var expected = source.PixelData[(y * CpcFontFile.PixelWidth + x) * 3] == 255 ? 1 : 0;
      Assert.That(bit, Is.EqualTo(expected), $"pixel {x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<CpcFontFile>(_Sheet(64, 200));

    Assert.That(file.RawData, Has.Length.EqualTo(CpcFontFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_GivesTheBitsToTheBrightPixels() {
    // A white sheet must come back with every bit set, not as its own negative.
    var white = new byte[CpcFontFile.PixelWidth * CpcFontFile.PixelHeight * 3];
    Array.Fill(white, (byte)255);

    var file = _Encode<CpcFontFile>(new() {
      Width = CpcFontFile.PixelWidth, Height = CpcFontFile.PixelHeight,
      Format = PixelFormat.Rgb24, PixelData = white,
    });

    Assert.That(file.RawData, Is.All.EqualTo((byte)0xFF));
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
