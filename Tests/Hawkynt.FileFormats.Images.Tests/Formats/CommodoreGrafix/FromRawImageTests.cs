using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CommodoreGrafix.Tests;

[TestFixture]
public sealed class CommodoreGrafixFileFromRawImageTests {

  /// <summary>One colour the frame shares and three a cell chooses, out of all sixteen.</summary>
  private static RawImage _Source(int columns, int rows) {
    var width = columns * 8;
    var height = rows * 8;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pattern = ((x >> 1) + y) & 3;
      var cell = (y >> 3) * columns + (x >> 3);
      var colour = pattern == 0 ? 0 : (cell * 3 + pattern) % Commodore64Graphics.ColorCount;
      if (colour == 0)
        colour = 15;

      var hex = Commodore64Graphics.HexColors[colour];
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(hex >> 16);
      rgb[at + 1] = (byte)(hex >> 8);
      rgb[at + 2] = (byte)hex;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(9, 5);
    var decoded = CommodoreGrafixFile.ToRawImage(CommodoreGrafixFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(72));
      Assert.That(decoded.Height, Is.EqualTo(40));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASizeThatIsNotWholeCellsIsSampledRatherThanRefused() {
    var decoded = CommodoreGrafixFile.ToRawImage(CommodoreGrafixFile.FromRawImage(_Source(9, 5).SampleTo(69, 37)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(72));
      Assert.That(decoded.Height, Is.EqualTo(40));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CommodoreGrafixFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheFrameIsCarriedInARiffContainerWhoseFormatChunkStatesItsShape() {
    // A C64 tool borrowing Windows' wrapper is the whole oddity of this format, and the format
    // chunk states the frame count twice — a file that contradicted itself would be rejected.
    var bytes = CommodoreGrafixWriter.ToBytes(CommodoreGrafixFile.FromRawImage(_Source(9, 5)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("CGFX"));
      Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("FRMT"));
      Assert.That(Encoding.ASCII.GetString(bytes, 32, 4), Is.EqualTo("DATA"));
      Assert.That(bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24), Is.EqualTo(bytes.Length - 8));
      Assert.That(bytes[20], Is.EqualTo(1));
      Assert.That(bytes[21], Is.EqualTo(1));
      Assert.That(bytes[24], Is.EqualTo(1));
      Assert.That(bytes[28], Is.EqualTo(5));
      Assert.That(bytes[29], Is.EqualTo(9));
      Assert.That(bytes[30], Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CommodoreGrafixFile.FromRawImage(_Source(9, 5));
    var restored = CommodoreGrafixReader.FromBytes(CommodoreGrafixWriter.ToBytes(file));

    Assert.That(
      _Rgb(CommodoreGrafixFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CommodoreGrafixFile.ToRawImage(file))));
  }
}
