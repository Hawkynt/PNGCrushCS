using System;
using FileFormat.Core;

namespace FileFormat.ComputerEyesSt.Tests;

[TestFixture]
public sealed class ComputerEyesStFileFromRawImageTests {

  /// <summary>A picture whose channels all sit on the six-bit grid the capture stores.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = ChannelScaling.Expand6((x + y) & 63);
      rgb[at + 1] = ChannelScaling.Expand6((x * 3) & 63);
      rgb[at + 2] = ChannelScaling.Expand6((y * 5) & 63);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(320, 200);
    var decoded = ComputerEyesStFile.ToRawImage(ComputerEyesStFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // A capture is one size because the digitiser sampled one column per television frame.
    var decoded = ComputerEyesStFile.ToRawImage(ComputerEyesStFile.FromRawImage(_Source(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => ComputerEyesStFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ThePictureIsStoredAColumnAtATimeAndSixBitsAChannel() {
    // Row order would give a picture that is unmistakably the right one and unmistakably wrong, so
    // the column stride is pinned against a pixel whose position and value are both known.
    var bytes = ComputerEyesStWriter.ToBytes(ComputerEyesStFile.FromRawImage(_Source(320, 200)));
    const int plane = 320 * ComputerEyesStFile.CapturedHeight;

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(ComputerEyesStFile.ColorFileSize));
      Assert.That(bytes[5], Is.EqualTo(0));
      Assert.That(bytes[ComputerEyesStFile.ColorOffset + 7 * ComputerEyesStFile.CapturedHeight + 3], Is.EqualTo(10));
      Assert.That(bytes[ComputerEyesStFile.ColorOffset + plane + 7 * ComputerEyesStFile.CapturedHeight + 3], Is.EqualTo(21));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = ComputerEyesStFile.FromRawImage(_Source(320, 200));
    var restored = ComputerEyesStReader.FromBytes(ComputerEyesStWriter.ToBytes(file));

    Assert.That(
      _Rgb(ComputerEyesStFile.ToRawImage(restored)), Is.EqualTo(_Rgb(ComputerEyesStFile.ToRawImage(file))));
  }
}
