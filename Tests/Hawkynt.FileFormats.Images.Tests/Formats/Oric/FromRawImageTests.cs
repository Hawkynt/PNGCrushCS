using System;
using FileFormat.Core;

namespace FileFormat.Oric.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture the screen can hold without spending a byte on colour: white ink on black paper, the
  /// pair every row starts with.
  /// </summary>
  private static RawImage _InkOnPaper(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      if (((x * 5 + y * 3) & 7) < 3) {
        var offset = (y * width + x) * 3;
        rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = 255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Bands of one colour six pixels wide, which an attribute byte can set as it draws them.</summary>
  private static RawImage _ColourBands(int width, int height) {
    ReadOnlySpan<int> palette = [0x000000, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF];
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = palette[(x / 6 + y) & 7];
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)(color >> 16);
      rgb[offset + 1] = (byte)(color >> 8);
      rgb[offset + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WhiteOnBlack_IsExact() {
    var source = _InkOnPaper(240, 200);

    var bytes = OricWriter.ToBytes(_Encode<OricFile>(source));
    var decoded = OricFile.ToRawImage(OricReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixPixelColourBands_IsExact() {
    var source = _ColourBands(240, 200);

    var bytes = OricWriter.ToBytes(_Encode<OricFile>(source));
    var decoded = OricFile.ToRawImage(OricReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var file = _Encode<OricFile>(_InkOnPaper(320, 240));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(240));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(OricWriter.ToBytes(file), Has.Length.EqualTo(40 * 200));
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
