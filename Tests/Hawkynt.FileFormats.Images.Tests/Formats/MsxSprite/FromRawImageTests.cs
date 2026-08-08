using System;
using FileFormat.Core;

namespace FileFormat.MsxSprite.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Checkerboard() {
    const int width = MsxSpriteFile.PixelWidth, height = MsxSpriteFile.PixelHeight;
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellOn = (((x / 8) + (y / 8)) & 1) != 0;
      var o = (y * width + x) * 3;
      var value = cellOn ? (byte)255 : (byte)0;
      data[o] = value;
      data[o + 1] = value;
      data[o + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CheckerboardOfSprites_ReproducesExactly() {
    var source = _Checkerboard();
    var file = MsxSpriteFile.FromRawImage(source);
    var restored = MsxSpriteReader.FromBytes(MsxSpriteWriter.ToBytes(file));
    var decoded = MsxSpriteFile.ToRawImage(restored);
    var decodedBgra = PixelConverter.Convert(decoded, PixelFormat.Bgra32);
    var sourceBgra = PixelConverter.Convert(source, PixelFormat.Bgra32);

    Assert.That(decodedBgra.PixelData, Is.EqualTo(sourceBgra.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => MsxSpriteFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesTheStandardDataLength()
    => Assert.That(MsxSpriteFile.FromRawImage(_Checkerboard()).RawData.Length, Is.EqualTo(MsxSpriteFile.ExpectedFileSize));
}
