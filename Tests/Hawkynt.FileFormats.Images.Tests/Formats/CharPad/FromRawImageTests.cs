using System;
using FileFormat.Core;

namespace FileFormat.CharPad.Tests;

[TestFixture]
public sealed class CharPadFileFromRawImageTests {

  /// <summary>Three colours the whole screen shares and a fourth that changes from cell to cell.</summary>
  private static RawImage _Source(int columns, int rows) {
    var width = columns * 8;
    var height = rows * 8;
    var rgb = new byte[width * height * 3];
    ReadOnlySpan<int> shared = [0, 1, 12];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pattern = ((x >> 1) + y) & 3;
      var cell = (y >> 3) * columns + (x >> 3);

      // Pattern 11 comes out of a colour byte whose fourth bit is the multicolour flag, so only the
      // low eight of the machine's sixteen can go there.
      var colour = pattern == 3 ? (cell % CharPadFile.CellColorCount) : shared[pattern];
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
    var source = _Source(11, 7);
    var decoded = CharPadFile.ToRawImage(CharPadFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(88));
      Assert.That(decoded.Height, Is.EqualTo(56));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASizeThatIsNotWholeCellsIsSampledRatherThanRefused() {
    var decoded = CharPadFile.ToRawImage(CharPadFile.FromRawImage(_Source(11, 7).SampleTo(85, 53)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(88));
      Assert.That(decoded.Height, Is.EqualTo(56));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CharPadFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void EveryCellGetsACharacterOfItsOwnAndAColourWithIt() {
    // The map names one character per cell in reading order, and the colour of each sits after all
    // eight rows of every character rather than beside its own.
    var file = CharPadFile.FromRawImage(_Source(11, 7));
    var bytes = CharPadWriter.ToBytes(file);
    var cells = 11 * 7;

    Assert.Multiple(() => {
      Assert.That(bytes[3], Is.EqualTo(CharPadFile.Version));
      Assert.That(bytes[8], Is.EqualTo(2));
      Assert.That(bytes[9], Is.EqualTo(4));
      Assert.That(bytes[10] | (bytes[11] << 8), Is.EqualTo(cells - 1));
      Assert.That(bytes.Length, Is.EqualTo(CharPadFile.CharactersOffset + cells * CharPadFile.CharacterLength + cells * 2));

      for (var cell = 0; cell < cells; ++cell) {
        var at = file.MapOffset + (cell << 1);
        Assert.That(bytes[at] | (bytes[at + 1] << 8), Is.EqualTo(cell));
        Assert.That(bytes[CharPadFile.CharactersOffset + (cells << 3) + cell], Is.LessThan(CharPadFile.CellColorCount));
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CharPadFile.FromRawImage(_Source(11, 7));
    var restored = CharPadReader.FromBytes(CharPadWriter.ToBytes(file));

    Assert.That(_Rgb(CharPadFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CharPadFile.ToRawImage(file))));
  }
}
