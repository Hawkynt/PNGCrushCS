using System;
using System.Collections.Generic;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class SixelCodecTests {

  [Test]
  [Category("Unit")]
  public void Decode_SingleChar_ProducesPixels() {
    var body = "#0;2;100;0;0~";
    var pixels = SixelCodec.Decode(body, out var width, out var height, out _, out _);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(1));
      Assert.That(height, Is.GreaterThan(0));
      Assert.That(pixels, Is.Not.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_Band_ProducesSixPixelsTall() {
    var body = "#0;2;100;0;0~";
    SixelCodec.Decode(body, out _, out var height, out _, out _);

    Assert.That(height, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void Decode_Rle_ExpandsCorrectly() {
    var body = "#0;2;100;0;0!5~";
    var pixels = SixelCodec.Decode(body, out var width, out var height, out _, out _);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(5));
      Assert.That(height, Is.EqualTo(6));
    });
  }

  [Test]
  [Category("Unit")]
  public void Encode_SingleBand_ProducesValidOutput() {
    var pixelData = new byte[6];
    var palette = new byte[] { 255, 0, 0 };

    var body = SixelCodec.Encode(pixelData, 1, 6, palette, 1);

    Assert.That(body, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Encode_Rle_CompressesRepeats() {
    var pixelData = new byte[60];
    var palette = new byte[] { 255, 0, 0 };

    var body = SixelCodec.Encode(pixelData, 10, 6, palette, 1);

    Assert.That(body, Does.Contain("!"));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SimpleImage() {
    var originalPixels = new byte[] { 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };
    var palette = new byte[] { 255, 0, 0, 0, 255, 0 };

    var body = SixelCodec.Encode(originalPixels, 2, 6, palette, 2);
    var decoded = SixelCodec.Decode(body, out var width, out var height, out _, out _);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(2));
      Assert.That(height, Is.EqualTo(6));
      for (var i = 0; i < originalPixels.Length; ++i)
        Assert.That(decoded[i], Is.EqualTo(originalPixels[i]), $"Pixel mismatch at index {i}");
    });
  }
}
