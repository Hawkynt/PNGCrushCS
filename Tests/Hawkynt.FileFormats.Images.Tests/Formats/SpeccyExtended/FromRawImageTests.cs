using System;
using FileFormat.Core;

namespace FileFormat.SpeccyExtended.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A channel value the file's five bits hold exactly, full scale being 24 rather than 31.</summary>
  private static byte _Level(int step) => (byte)Math.Min(255, step * 255 / SpeccyExtendedFile.ChannelFullScale);

  private static RawImage _SixteenColours(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x + y) & 15;
      var offset = (y * width + x) * 3;
      rgb[offset] = _Level(index);
      rgb[offset + 1] = _Level((index * 3 + 1) % 25);
      rgb[offset + 2] = _Level(24 - index);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColoursOnTheFiveBitLadder_IsExact() {
    var source = _SixteenColours(64, 48);

    var bytes = SpeccyExtendedWriter.ToBytes(_Encode<SpeccyExtendedFile>(source));
    var decoded = SpeccyExtendedFile.ToRawImage(SpeccyExtendedReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(64));
      Assert.That(decoded.Height, Is.EqualTo(48));
    });

    for (var i = 0; i < 64 * 48; ++i) {
      var entry = decoded.PixelData[i] * 3;
      Assert.That(decoded.Palette![entry], Is.EqualTo(source.PixelData[i * 3]), $"pixel {i} red");
      Assert.That(decoded.Palette[entry + 1], Is.EqualTo(source.PixelData[i * 3 + 1]), $"pixel {i} green");
      Assert.That(decoded.Palette[entry + 2], Is.EqualTo(source.PixelData[i * 3 + 2]), $"pixel {i} blue");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The file states its own size, so nothing has to be sampled away.
    var file = _Encode<SpeccyExtendedFile>(_SixteenColours(320, 240));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(240));
      Assert.That(file.PixelData, Has.Length.EqualTo(320 * 240));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TakesAnOddPixelCount() {
    // Two pixels share a byte, so a picture with an odd number of them must still write and read.
    var source = _SixteenColours(9, 7);
    var decoded = SpeccyExtendedFile.ToRawImage(
      SpeccyExtendedReader.FromBytes(SpeccyExtendedWriter.ToBytes(_Encode<SpeccyExtendedFile>(source))));

    Assert.That(decoded.PixelData, Has.Length.EqualTo(63));
  }

  /// <summary>
  /// Encodes through the interface rather than the type, so this stops compiling if the declaration
  /// goes away — which is what the registry generator reads to decide the format can be written at
  /// all, and nothing else here would notice its absence.
  /// </summary>
  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
