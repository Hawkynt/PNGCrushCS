using System;
using FileFormat.Core;

namespace FileFormat.Calamus.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _BlackAndWhite(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var ink = ((x * 3 + y * 7) % 5) == 0;
      var offset = (y * width + x) * 3;
      rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = ink ? (byte)0 : (byte)255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ATwoColourPage_IsExact() {
    var source = _BlackAndWhite(200, 120);

    var bytes = CalamusWriter.ToBytes(_Encode<CalamusFile>(source));
    var decoded = CalamusFile.ToRawImage(CalamusReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(200));
      Assert.That(decoded.Height, Is.EqualTo(120));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TakesAWidthThatIsNoMultipleOfEight() {
    // Rows are padded to a byte, so an odd width must still produce a file the reader takes back.
    var source = _BlackAndWhite(37, 11);
    var decoded = CalamusFile.ToRawImage(CalamusReader.FromBytes(CalamusWriter.ToBytes(_Encode<CalamusFile>(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnotherSize() {
    var source = _BlackAndWhite(640, 480);
    var decoded = CalamusFile.ToRawImage(CalamusReader.FromBytes(CalamusWriter.ToBytes(_Encode<CalamusFile>(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(640));
      Assert.That(decoded.Height, Is.EqualTo(480));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
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
