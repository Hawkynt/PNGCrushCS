using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Imagic;

namespace FileFormat.Imagic.Tests;

[TestFixture]
public sealed class ImagicTests {

  private static byte[] _Screen(Func<int, byte> fill) {
    var screen = new byte[ImagicCompressor.ScreenSize];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = fill(i);

    return screen;
  }

  private static ImagicFile _Sample(byte[] screen, ImagicResolution resolution = ImagicResolution.Low) => new() {
    Resolution = resolution,
    Palette = new short[ImagicFile.PaletteCount],
    Reserved = new byte[ImagicFile.ReservedSize],
    ScreenData = screen,
  };

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 2] = (byte)(i % 173);
      data[i + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void Compress_RoundTripsAUniformScreen() {
    var screen = _Screen(_ => 0);
    var (data, escape) = ImagicCompressor.Compress(screen);

    Assert.That(ImagicCompressor.Decompress(data, escape), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RoundTripsAVariedScreen() {
    var screen = _Screen(i => (byte)(i * 31 % 256));
    var (data, escape) = ImagicCompressor.Compress(screen);

    Assert.That(ImagicCompressor.Decompress(data, escape), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RoundTripsAScreenUsingEveryByteValue() {
    // Forces an escape byte that also appears in the data, so literals have to be doubled.
    var screen = _Screen(i => (byte)(i % 256));
    var (data, escape) = ImagicCompressor.Compress(screen);

    Assert.That(ImagicCompressor.Decompress(data, escape), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void Compress_CollapsesAUniformScreen() {
    var (data, _) = ImagicCompressor.Compress(_Screen(_ => 0));

    Assert.That(data, Has.Length.LessThan(1000), "a flat screen is nothing but long runs");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheImdcTagAndStamp() {
    var bytes = ImagicWriter.ToBytes(_Sample(_Screen(_ => 0)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("IMDC"));
      Assert.That(bytes[4], Is.Zero);
      Assert.That(bytes[ImagicFile.StampOffset], Is.EqualTo(200));
      Assert.That(bytes[ImagicFile.StampOffset + 1], Is.EqualTo(2));
    });
  }

  [TestCase(ImagicResolution.Low)]
  [TestCase(ImagicResolution.Medium)]
  [TestCase(ImagicResolution.High)]
  [Category("Unit")]
  public void RoundTrip_PreservesTheResolutionAndScreen(ImagicResolution resolution) {
    var file = _Sample(_Screen(i => (byte)(i * 17 % 256)), resolution);
    var restored = ImagicReader.FromBytes(ImagicWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Resolution, Is.EqualTo(resolution));
      Assert.That(restored.ScreenData, Is.EqualTo(file.ScreenData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAFileWithoutTheTag()
    => Assert.Throws<InvalidDataException>(() => ImagicReader.FromBytes(new byte[128]));

  [TestCase(320, 200, ImagicResolution.Low)]
  [TestCase(640, 200, ImagicResolution.Medium)]
  [TestCase(640, 400, ImagicResolution.High)]
  [Category("Unit")]
  public void FromRawImage_PicksTheResolutionFromTheSize(int width, int height, ImagicResolution expected) {
    var file = ImagicFile.FromRawImage(_Gradient(width, height));

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(expected));
      Assert.That(file.ScreenData, Has.Length.EqualTo(ImagicCompressor.ScreenSize));
      Assert.That(() => ImagicReader.FromBytes(ImagicWriter.ToBytes(file)), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsASizeTheStCannotShow()
    => Assert.Throws<ArgumentException>(() => ImagicFile.FromRawImage(_Gradient(320, 240)));
}
