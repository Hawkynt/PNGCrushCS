using System;
using FileFormat.Core;

namespace FileFormat.FunPainter.Tests;

[TestFixture]
public sealed class FunPainterFromRawImageTests {

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  /// <summary>A picture the format demonstrably can hold, being one it was just read out of.</summary>
  /// <remarks>
  /// A picture this format can hold cannot be drawn freehand: every colour has to be the average of
  /// two of the machine's sixteen, the two fields have to agree about the columns they share, and
  /// each character cell has to stay inside one colour memory entry and two colours a line. Decoding
  /// a file is the only way to get one, so that is what this does.
  /// </remarks>
  private static RawImage _WhatTheFormatHolds()
    => FunPainterFile.ToRawImage(FunPainterReader.FromBytes(FunPainterProbe.Unpacked()));

  private static RawImage _Stripes(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = x % 2 == 0
        ? 0
        : Commodore64Graphics.HexColors[(x / 4 + y / 8 * 3) % Commodore64Graphics.ColorCount];

      var at = (y * width + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// The whole point of reading the blend back apart: a picture the two fields can show comes back
  /// exactly, so reading a file and writing it again loses nothing.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _WhatTheFormatHolds();
    var decoded = FunPainterFile.ToRawImage(FunPainterFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FunPainterFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FunPainterFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheExactPathReproducesAPictureTheFormatCanHold() {
    var source = _WhatTheFormatHolds();
    var decoded = FunPainterFile.ToRawImage(FunPainterFile.FromRawImageExact(source));

    Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = FunPainterFile.ToRawImage(FunPainterFile.FromRawImage(_Stripes(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FunPainterFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FunPainterFile.Height));
    });
  }

  /// <summary>
  /// A picture the machine cannot show is approximated on the registry path and refused by name on
  /// the exact one, rather than either path pretending.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void AColourTheMachineCannotBlend_IsApproximatedButNotAcceptedAsExact() {
    // Sharp black-and-colour columns are exactly what interlacing cannot do: the two fields share
    // every even column, so a column of pure colour beside a column of pure black asks one field
    // pixel to be both at once.
    var arbitrary = _Stripes(FunPainterFile.Width, FunPainterFile.Height);

    Assert.Multiple(() => {
      Assert.That(() => FunPainterFile.FromRawImage(arbitrary), Throws.Nothing);

      var refusal = Assert.Throws<ArgumentException>(() => FunPainterFile.FromRawImageExact(arbitrary));
      Assert.That(refusal!.Message, Does.Contain("Fun Painter cannot show"));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureOfTheWrongSize_IsRefusedByTheExactPath() {
    var refusal = Assert.Throws<ArgumentException>(
      () => FunPainterFile.FromRawImageExact(_Stripes(320, 200)));

    Assert.That(refusal!.Message, Does.Contain("296x200"));
  }

  /// <summary>The encoder is public, so a caller can reach it without a picture of the right size.</summary>
  [Test]
  [Category("Unit")]
  public void TheEncoderRefusesAPictureShorterThanTheScreen() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => FunPainterEncoder.Encode(new byte[100]));
      Assert.Throws<ArgumentException>(() => FunPainterEncoder.EncodeExact(new byte[100]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FunPainterFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImageExact_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FunPainterFile.FromRawImageExact(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FunPainterFile.FromRawImage(_Stripes(FunPainterFile.Width, FunPainterFile.Height));
    var restored = FunPainterReader.FromBytes(FunPainterWriter.ToBytes(file));

    Assert.That(_Rgb(FunPainterFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FunPainterFile.ToRawImage(file))));
  }
}
