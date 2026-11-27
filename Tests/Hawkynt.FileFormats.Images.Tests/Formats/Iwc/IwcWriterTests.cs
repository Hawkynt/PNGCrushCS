using System;
using FileFormat.Core;

namespace FileFormat.Iwc.Tests;

[TestFixture]
public sealed class IwcWriterTests {

  /// <summary>The fixtures are this writer's output that the applet has decoded; the writer must not drift from them.</summary>
  [TestCase("synth_37x23", 0)]
  [TestCase("checker_16x12_l1", 1)]
  [TestCase("tiny_2x2", 0)]
  [Category("Integration")]
  public void Writer_StillProducesTheBytesTheAppletDecoded(string name, int levels) {
    var picture = IwcTestSupport.ReadPpm(IwcTestSupport.Fixture(name + ".ppm"));
    var bytes = IwcWriter.ToBytes(new IwcFile { Width = picture.Width, Height = picture.Height, Levels = levels, PixelData = picture.Pixels });
    Assert.That(bytes, Is.EqualTo(IwcTestSupport.Fixture(name + ".iwc")));
  }

  [TestCase(1, 1)]
  [TestCase(1, 5)]
  [TestCase(5, 1)]
  [Category("Unit")]
  public void PicturesWithASideOfOne_AreRefusedByName(int width, int height) {
    var file = new IwcFile { Width = width, Height = height, PixelData = new byte[width * height * 3] };
    var exception = Assert.Throws<ArgumentException>(() => IwcWriter.ToBytes(file));
    Assert.That(exception!.Message, Does.Contain("two samples"));
  }

  [Test]
  [Category("Unit")]
  public void LevelsThePictureCannotTake_AreRefused() {
    var file = new IwcFile { Width = 8, Height = 8, Levels = 4, PixelData = new byte[8 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => IwcWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ShortPixelData_IsRefused() {
    var file = new IwcFile { Width = 4, Height = 4, PixelData = new byte[10] };
    Assert.Throws<ArgumentException>(() => IwcWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void AutomaticLevels_StopAtSevenOrWhereThePictureRunsOut() {
    Assert.Multiple(() => {
      Assert.That(IwcReader.FromSpan(IwcWriter.ToBytes(new IwcFile { Width = 400, Height = 300, PixelData = new byte[400 * 300 * 3] })).Levels, Is.EqualTo(7));
      Assert.That(IwcReader.FromSpan(IwcWriter.ToBytes(new IwcFile { Width = 9, Height = 300, PixelData = new byte[9 * 300 * 3] })).Levels, Is.EqualTo(3));
      Assert.That(IwcReader.FromSpan(IwcWriter.ToBytes(new IwcFile { Width = 2, Height = 2, PixelData = new byte[12] })).Levels, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void GreyPictureWhoseBandsFitTheByteRange_IsLossless() {
    var pixels = new byte[33 * 21 * 3];
    for (var i = 0; i < 33 * 21; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(40 + i % 33 * 3 + i / 33 * 4 + (i % 33 & 1) * 9);

    var decoded = IwcReader.FromSpan(IwcWriter.ToBytes(new IwcFile { Width = 33, Height = 21, PixelData = pixels }));
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void SplayCoder_RoundTripsLiteralsRunsAndLongRuns() {
    var random = new Random(554589);
    var symbols = new byte[20_000];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = (byte)(i < 7000 ? random.Next(253) : i < 13_500 ? 17 : random.Next(4) == 0 ? 252 : 0);

    var coded = IwcSplayCoder.Encode(symbols);
    Assert.That(IwcSplayCoder.Decode(coded, symbols.Length), Is.EqualTo(symbols));
  }

  [Test]
  [Category("Unit")]
  public void SplayCoder_RefusesValuesItCannotCarry()
    => Assert.Throws<ArgumentOutOfRangeException>(() => IwcSplayCoder.Encode(new byte[] { 253 }));
}
