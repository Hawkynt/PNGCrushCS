using System;
using FileFormat.Core;

namespace FileFormat.NokiaGroupGraphics.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _BlackAndWhite(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var ink = ((x + y * 2) & 3) == 0;
      var offset = (y * width + x) * 3;
      rgb[offset] = rgb[offset + 1] = rgb[offset + 2] = ink ? (byte)0 : (byte)255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ATwoColourLogo_IsExact() {
    var source = _BlackAndWhite(72, 28);

    var bytes = NokiaGroupGraphicsWriter.ToBytes(_Encode<NokiaGroupGraphicsFile>(source));
    var decoded = NokiaGroupGraphicsFile.ToRawImage(NokiaGroupGraphicsReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(72));
      Assert.That(decoded.Height, Is.EqualTo(28));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesDownWhatTheHeaderCannotName() {
    var file = _Encode<NokiaGroupGraphicsFile>(_BlackAndWhite(640, 480));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(byte.MaxValue));
      Assert.That(file.Height, Is.EqualTo(byte.MaxValue));
      Assert.That(file.PixelData, Has.Length.EqualTo(32 * 255));
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
