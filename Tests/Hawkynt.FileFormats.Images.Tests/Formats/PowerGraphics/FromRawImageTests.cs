using System;
using FileFormat.Core;

namespace FileFormat.PowerGraphics.Tests;

[TestFixture]
public sealed class PowerGraphicsFileFromRawImageTests {

  /// <summary>Screen pixels at each end of a line that ANTIC never fetches.</summary>
  private const int _BORDER = 8;

  /// <summary>The four colour registers a mode E scanline draws from, background first.</summary>
  private static ReadOnlySpan<byte> _Colors => [0x00, 0x36, 0x8A, 0xCE];

  /// <summary>
  /// A picture the format holds outright: four colours a scanline, in bands wide enough that no
  /// band boundary falls inside one of the two-pixel-wide pixels mode E draws.
  /// </summary>
  /// <remarks>
  /// The border takes the background whatever the picture says, so the bands begin with it — a
  /// picture that asked for anything else at the ends could not come back unchanged, and would be
  /// testing the border rather than the encoder.
  /// </remarks>
  private static RawImage _Bands() {
    var gtia = Atari8BitGraphics.Palette;
    var rgb = new byte[PowerGraphicsFile.Width * PowerGraphicsFile.Height * 3];

    for (var y = 0; y < PowerGraphicsFile.Height; ++y)
    for (var x = 0; x < PowerGraphicsFile.Width; ++x) {
      var band = x < _BORDER || x >= PowerGraphicsFile.Width - _BORDER
        ? _Colors[0]
        : _Colors[(x - _BORDER) / 80 % _Colors.Length];

      var at = (y * PowerGraphicsFile.Width + x) * 3;
      rgb[at] = gtia[band * 3];
      rgb[at + 1] = gtia[band * 3 + 1];
      rgb[at + 2] = gtia[band * 3 + 2];
    }

    return new() {
      Width = PowerGraphicsFile.Width,
      Height = PowerGraphicsFile.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = _Bands();
    var decoded = PowerGraphicsFile.ToRawImage(PowerGraphicsFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(PowerGraphicsFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(PowerGraphicsFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = PowerGraphicsFile.ToRawImage(PowerGraphicsFile.FromRawImage(_Bands().SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(PowerGraphicsFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(PowerGraphicsFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void APlainRgbPictureIsAccepted() {
    var rgb = new byte[37 * 23 * 3];
    for (var pixel = 0; pixel < 37 * 23; ++pixel) {
      rgb[pixel * 3] = (byte)(pixel * 7);
      rgb[pixel * 3 + 1] = (byte)(pixel * 3);
      rgb[pixel * 3 + 2] = (byte)(pixel * 11);
    }

    var file = PowerGraphicsFile.FromRawImage(
      new() { Width = 37, Height = 23, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(PowerGraphicsWriter.ToBytes(file), Has.Length.EqualTo(PowerGraphicsEncoder.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void TheEndsOfEveryLineShowTheBackgroundWhateverThePictureAsks() {
    // The quirk: ANTIC fetches forty bytes, which is three hundred and twenty of the three hundred
    // and thirty-six pixels the raster instructions can reach. The eight at each end are outside
    // what it fetches and show the background register, so a picture with anything else there is not
    // refused — it is simply not drawn.
    var gtia = Atari8BitGraphics.Palette;
    var rgb = new byte[PowerGraphicsFile.Width * PowerGraphicsFile.Height * 3];
    for (var y = 0; y < PowerGraphicsFile.Height; ++y)
    for (var x = 0; x < PowerGraphicsFile.Width; ++x) {
      var at = (y * PowerGraphicsFile.Width + x) * 3;
      var wanted = x < _BORDER ? 0x28 : x >= PowerGraphicsFile.Width - _BORDER ? 0xCA : 0x66;
      rgb[at] = gtia[wanted * 3];
      rgb[at + 1] = gtia[wanted * 3 + 1];
      rgb[at + 2] = gtia[wanted * 3 + 2];
    }

    var decoded = _Rgb(PowerGraphicsFile.ToRawImage(PowerGraphicsFile.FromRawImage(
      new() { Width = PowerGraphicsFile.Width, Height = PowerGraphicsFile.Height, Format = PixelFormat.Rgb24, PixelData = rgb })));

    for (var y = 0; y < PowerGraphicsFile.Height; ++y) {
      var row = y * PowerGraphicsFile.Width * 3;
      for (var x = 0; x < _BORDER; ++x) {
        var left = row + x * 3;
        var right = row + (PowerGraphicsFile.Width - _BORDER + x) * 3;
        Assert.That(decoded.AsSpan(right, 3).SequenceEqual(decoded.AsSpan(left, 3)), Is.True, $"{x},{y}");
        Assert.That(decoded.AsSpan(left, 3).SequenceEqual(decoded.AsSpan(row, 3)), Is.True, $"{x},{y}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = PowerGraphicsFile.FromRawImage(_Bands());
    var bytes = PowerGraphicsWriter.ToBytes(file);
    var restored = PowerGraphicsReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Columns, Is.EqualTo(40));
      Assert.That(file.DmaControl & 12, Is.Zero, "no sprite fetching, so no cycles lost to it");
      Assert.That(
        _Rgb(PowerGraphicsFile.ToRawImage(restored)),
        Is.EqualTo(_Rgb(PowerGraphicsFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => PowerGraphicsFile.FromRawImage(null!));
}
