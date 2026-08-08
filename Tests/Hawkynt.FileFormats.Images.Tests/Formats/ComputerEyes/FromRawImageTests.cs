using System;
using FileFormat.Core;

namespace FileFormat.ComputerEyes.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _GrayRamp(int width, int height) {
    var data = new byte[width * height];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayRamp_ReproducesExactly() {
    var source = _GrayRamp(23, 9);
    var file = ComputerEyesFile.FromRawImage(source);
    var restored = ComputerEyesReader.FromBytes(ComputerEyesWriter.ToBytes(file));
    var decoded = ComputerEyesFile.ToRawImage(restored);

    Assert.That(decoded.Width, Is.EqualTo(23));
    Assert.That(decoded.Height, Is.EqualTo(9));
    for (var i = 0; i < source.PixelData.Length; ++i)
      Assert.That(decoded.PixelData[i * 3], Is.EqualTo(source.PixelData[i]), $"pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ReducesColourToTheOneByteAPixelTheDigitiserProduced() {
    // The scanner had no colour, so a colour picture becomes grey rather than being refused.
    var color = new RawImage {
      Width = 2, Height = 2, Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]
    };

    var file = ComputerEyesFile.FromRawImage(color);

    Assert.Multiple(() => {
      Assert.That(file.PixelData, Has.Length.EqualTo(4));
      Assert.That(file.PixelData[3], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var file = ComputerEyesFile.FromRawImage(_GrayRamp(100, 7));

    Assert.That((file.Width, file.Height), Is.EqualTo((100, 7)));
  }
}
