using System;
using FileFormat.Core;

namespace FileFormat.Fuckpaint.Tests;

[TestFixture]
public sealed class FuckpaintFileFromRawImageTests {

  /// <summary>
  /// A picture that is one colour across each row and changes colour a character cell at a time,
  /// with black over half of it so that the shared background is black — and a leftmost column
  /// holding what the format shows there, which is the row's colour averaged with that background.
  /// </summary>
  /// <remarks>
  /// The second field is displaced, so its leftmost screen column has nothing behind it and shows
  /// the background whatever the file says. A picture the format can hold exactly therefore has to
  /// agree with it about that one column.
  /// </remarks>
  private static RawImage _Source() {
    var rgb = new byte[FuckpaintFile.Width * FuckpaintFile.Height * 3];

    for (var y = 0; y < FuckpaintFile.Height; ++y) {
      var colour = (y & 1) == 0 ? 0 : Commodore64Graphics.HexColors[1 + (y >> 3) % 15];

      for (var x = 0; x < FuckpaintFile.Width; ++x) {
        var at = (y * FuckpaintFile.Width + x) * 3;
        for (var channel = 0; channel < 3; ++channel) {
          var value = (byte)(colour >> ((2 - channel) * 8));
          rgb[at + channel] = x == 0 ? (byte)(value >> 1) : value;
        }
      }
    }

    return new() {
      Width = FuckpaintFile.Width, Height = FuckpaintFile.Height, Format = PixelFormat.Rgb24, PixelData = rgb,
    };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source();
    var decoded = FuckpaintFile.ToRawImage(FuckpaintFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FuckpaintFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FuckpaintFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = FuckpaintFile.ToRawImage(FuckpaintFile.FromRawImage(_Source().SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FuckpaintFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FuckpaintFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FuckpaintFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheLeftmostColumnShowsTheBackgroundBlendedIn() {
    // The second field is displaced, so its leftmost column has nothing behind it. No choice of
    // bytes controls what shows there: it is the first field's pixel averaged with the background,
    // which is why a picture the format holds exactly has to agree with it about that column.
    var file = FuckpaintFile.FromRawImage(_Source());
    var decoded = _Rgb(FuckpaintFile.ToRawImage(file));
    var bytes = FuckpaintWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(FuckpaintFile.FileSize));
      Assert.That(bytes[FuckpaintFile.BackgroundOffset] & 15, Is.EqualTo(0));

      for (var y = 1; y < FuckpaintFile.Height; y += 2) {
        var left = (y * FuckpaintFile.Width) * 3;
        var next = left + 3;
        Assert.That(decoded[left], Is.EqualTo(decoded[next] >> 1), $"row {y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FuckpaintFile.FromRawImage(_Source());
    var restored = FuckpaintReader.FromBytes(FuckpaintWriter.ToBytes(file));

    Assert.That(_Rgb(FuckpaintFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FuckpaintFile.ToRawImage(file))));
  }
}
