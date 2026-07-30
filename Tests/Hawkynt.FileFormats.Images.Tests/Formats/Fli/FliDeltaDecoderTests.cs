using System;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class FliDeltaDecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_AllSame_DecodesCorrectly() {
    // 4x2 image, all pixels = 42
    var width = 4;
    var height = 2;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 42;

    var encoded = FliDeltaEncoder.EncodeByteRun(pixels, width, height);
    var decoded = FliDeltaDecoder.DecodeByteRun(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_AllDifferent_DecodesCorrectly() {
    var width = 4;
    var height = 2;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);

    var encoded = FliDeltaEncoder.EncodeByteRun(pixels, width, height);
    var decoded = FliDeltaDecoder.DecodeByteRun(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_MixedContent_RoundTrips() {
    var width = 10;
    var height = 3;
    var pixels = new byte[width * height];
    // Mix of runs and literals
    for (var i = 0; i < 5; ++i)
      pixels[i] = 100; // run
    for (var i = 5; i < 10; ++i)
      pixels[i] = (byte)(i * 7); // literals
    for (var i = 10; i < 20; ++i)
      pixels[i] = 200; // run
    for (var i = 20; i < 30; ++i)
      pixels[i] = (byte)(i * 3); // literals

    var encoded = FliDeltaEncoder.EncodeByteRun(pixels, width, height);
    var decoded = FliDeltaDecoder.DecodeByteRun(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_SinglePixelRow_RoundTrips() {
    var width = 1;
    var height = 1;
    var pixels = new byte[] { 99 };

    var encoded = FliDeltaEncoder.EncodeByteRun(pixels, width, height);
    var decoded = FliDeltaDecoder.DecodeByteRun(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_LargeImage_RoundTrips() {
    var width = 320;
    var height = 200;
    var pixels = new byte[width * height];
    var rng = new Random(42);
    rng.NextBytes(pixels);

    var encoded = FliDeltaEncoder.EncodeByteRun(pixels, width, height);
    var decoded = FliDeltaDecoder.DecodeByteRun(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void EncodeByteRun_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliDeltaEncoder.EncodeByteRun(null!, 1, 1));
  }

  [Test]
  [Category("Unit")]
  public void DecodeByteRun_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliDeltaDecoder.DecodeByteRun(null!, 1, 1));
  }
}
