using System;
using FileFormat.Core;

namespace FileFormat.NewsRoom.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 128;
  private const int _HEIGHT = 96;

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
  public void RoundTrip_ATwoTonePicture_ReturnsEveryPixelUnchanged() {
    var source = _Stripes(_WIDTH, _HEIGHT);

    var restored = PixelConverter.Convert(
      NewsRoomFile.ToRawImage(NewsRoomReader.FromBytes(NewsRoomWriter.ToBytes(NewsRoomFile.FromRawImage(source)))),
      PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>
  /// Both sizes stand in the header as a pair of single-byte coordinates, so a panel cannot be
  /// wider than 256 or taller than 248; a larger picture is sampled onto one that size.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_ALargerPicture_IsSampledOntoWhatTheHeaderCanState() {
    var file = NewsRoomFile.FromRawImage(_Stripes(640, 480));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(NewsRoomFile.MaximumWidth));
      Assert.That(file.Height, Is.EqualTo(NewsRoomFile.MaximumHeight));
      Assert.That(NewsRoomWriter.ToBytes(file),
        Has.Length.EqualTo(NewsRoomFile.HeaderSize + NewsRoomFile.StrideOf(NewsRoomFile.MaximumWidth) * NewsRoomFile.MaximumHeight));
    });
  }

  /// <summary>A size that is not a whole number of bytes across is rounded up to one.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_ASizeOffAByteBoundary_IsRoundedUp() {
    var file = NewsRoomFile.FromRawImage(_Stripes(37, 21));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(40));
      Assert.That(file.Height, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AWhitePixel_SetsItsBit() {
    var pixels = new byte[_WIDTH * _HEIGHT];
    pixels[0] = 255;

    var file = NewsRoomFile.FromRawImage(new() {
      Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Gray8, PixelData = pixels
    });

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(0x80));
      Assert.That(file.PixelData[1], Is.EqualTo(0x00));
    });
  }
}
