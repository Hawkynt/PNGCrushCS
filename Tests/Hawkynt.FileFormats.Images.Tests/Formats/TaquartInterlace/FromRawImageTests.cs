using System;
using FileFormat.Core;

namespace FileFormat.TaquartInterlace.Tests;

[TestFixture]
public sealed class TaquartInterlaceFileFromRawImageTests {

  /// <summary>Stored size of the handmade picture; it is displayed at twice this both ways.</summary>
  private const int _STORED_WIDTH = 52;

  private const int _STORED_HEIGHT = 39;

  /// <summary>
  /// A picture built as a file, because the three fields are phased against each other and no
  /// drawing says which of them a given displayed column came from.
  /// </summary>
  /// <remarks>
  /// All three fields are filled with something that repeats on no useful period, so every hue and
  /// every luminance is in play and nothing the encoder recovers can be recovered by accident. The
  /// stored width is deliberately not the maximum: the format holds any size up to it.
  /// </remarks>
  private static TaquartInterlaceFile _Handmade() {
    var stride = _STORED_WIDTH >> 2;
    var fieldLength = stride * _STORED_HEIGHT;
    var data = new byte[TaquartInterlaceFile.FieldsOffset + fieldLength * 3];
    TaquartInterlaceFile.Signature.CopyTo(data);
    data[5] = _STORED_WIDTH;
    data[6] = _STORED_HEIGHT;
    data[7] = (byte)fieldLength;
    data[8] = (byte)(fieldLength >> 8);

    for (var at = 0; at < fieldLength * 3; ++at)
      data[TaquartInterlaceFile.FieldsOffset + at] = (byte)(at * 37 ^ at >> 3 ^ at / fieldLength * 91);

    return TaquartInterlaceReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    // Exact, which the three fields' phasing might not have allowed: a Graphics 10 nibble is shared
    // between the pair of nibbles to its left and the pair to its right, so a greedy encoder would
    // settle one and spoil the other. One dynamic-programming pass along the row settles both.
    var source = TaquartInterlaceFile.ToRawImage(_Handmade());
    var decoded = TaquartInterlaceFile.ToRawImage(TaquartInterlaceFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(_STORED_WIDTH * 2));
      Assert.That(decoded.Height, Is.EqualTo(_STORED_HEIGHT * 2));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // 101 is neither even nor a multiple of the four stored pixels a nibble pair covers, so the
    // stored width is the nearest the format can state and the picture is sampled to suit.
    var file = TaquartInterlaceFile.FromRawImage(TaquartInterlaceFile.ToRawImage(_Handmade()).SampleTo(101, 77));
    var decoded = TaquartInterlaceFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.StoredWidth, Is.EqualTo(52));
      Assert.That(file.StoredHeight, Is.EqualTo(39));
      Assert.That(decoded.Width, Is.EqualTo(104));
      Assert.That(decoded.Height, Is.EqualTo(78));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureLargerThanTheFormatHoldsIsSampledDownToItsMaximum() {
    var file = TaquartInterlaceFile.FromRawImage(
      TaquartInterlaceFile.ToRawImage(_Handmade()).SampleTo(640, 480));

    Assert.Multiple(() => {
      Assert.That(file.StoredWidth, Is.EqualTo(TaquartInterlaceFile.MaxStoredWidth));
      Assert.That(file.StoredHeight, Is.EqualTo(TaquartInterlaceFile.MaxStoredHeight));
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

    var file = TaquartInterlaceFile.FromRawImage(
      new() { Width = 37, Height = 23, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(
      TaquartInterlaceWriter.ToBytes(file),
      Has.Length.EqualTo(TaquartInterlaceFile.FieldsOffset + file.FieldLength * 3));
  }

  [Test]
  [Category("Unit")]
  public void TheTopRowIsHalfLitWhateverThePictureAsks() {
    // The quirk the format cannot be argued out of: an even displayed row has no luminance of its
    // own and takes the mean of the rows above and below it. The topmost row has nothing above it,
    // which counts as black — so whatever brightness a picture asks for at the top, it gets half of
    // it, and asking for a flat colour everywhere proves it.
    var gtia = Atari8BitGraphics.Palette;
    var rgb = new byte[320 * 238 * 3];
    for (var pixel = 0; pixel < 320 * 238; ++pixel) {
      rgb[pixel * 3] = gtia[0x9E * 3];
      rgb[pixel * 3 + 1] = gtia[0x9E * 3 + 1];
      rgb[pixel * 3 + 2] = gtia[0x9E * 3 + 2];
    }

    var decoded = _Rgb(TaquartInterlaceFile.ToRawImage(TaquartInterlaceFile.FromRawImage(
      new() { Width = 320, Height = 238, Format = PixelFormat.Rgb24, PixelData = rgb })));

    long top = 0, below = 0;
    for (var x = 0; x < 320 * 3; ++x) {
      top += decoded[x];
      below += decoded[320 * 3 * 3 + x];
    }

    Assert.Multiple(() => {
      Assert.That(top, Is.LessThan(below), "the top row cannot be as bright as the rows under it");
      Assert.That(below / (double)(320 * 3), Is.GreaterThan(64.0), "the rows under it are not dimmed");
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = TaquartInterlaceFile.FromRawImage(TaquartInterlaceFile.ToRawImage(_Handmade()));
    var bytes = TaquartInterlaceWriter.ToBytes(file);
    var restored = TaquartInterlaceReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes[..TaquartInterlaceFile.Signature.Length], Is.EqualTo(TaquartInterlaceFile.Signature.ToArray()));
      Assert.That(bytes[5], Is.EqualTo(_STORED_WIDTH));
      Assert.That(bytes[6], Is.EqualTo(_STORED_HEIGHT));
      Assert.That(
        _Rgb(TaquartInterlaceFile.ToRawImage(restored)),
        Is.EqualTo(_Rgb(TaquartInterlaceFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => TaquartInterlaceFile.FromRawImage(null!));
}
