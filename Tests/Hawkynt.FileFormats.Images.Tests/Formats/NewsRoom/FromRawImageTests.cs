using System;
using FileFormat.Core;

namespace FileFormat.NewsRoom.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 192;

  private static RawImage _Stripes(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (x / 5 + y / 7) % 2 == 0 ? (byte)0 : (byte)255;
        var offset = (y * width + x) * 3;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PanelSizedTwoTonePicture_ReturnsEveryPixelUnchanged() {
    var source = _Stripes(_WIDTH, _HEIGHT);

    var restored = NewsRoomFile.ToRawImage(NewsRoomReader.FromBytes(NewsRoomWriter.ToBytes(NewsRoomFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The file is nothing but the panel and states no size, so a picture of another size is
  /// sampled onto it rather than refused.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoThePanel([Values(40, 100, 640)] int width) {
    var file = NewsRoomFile.FromRawImage(_Stripes(width, width / 2));

    Assert.Multiple(() => {
      Assert.That(file.PixelData, Has.Length.EqualTo(NewsRoomFile.ExpectedFileSize));
      Assert.That(NewsRoomWriter.ToBytes(file), Has.Length.EqualTo(NewsRoomFile.ExpectedFileSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_BlackPixel_SetsItsBit() {
    var pixels = new byte[_WIDTH * _HEIGHT];
    Array.Fill(pixels, (byte)255);
    pixels[0] = 0;

    var file = NewsRoomFile.FromRawImage(new() {
      Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Gray8, PixelData = pixels
    });

    Assert.That(file.PixelData[0], Is.EqualTo(0x80));
  }
}
