using System;
using FileFormat.Core;

namespace FileFormat.Graph2Font.Tests;

[TestFixture]
public sealed class Graph2FontFileFromRawImageTests {

  private const int _COLUMNS = 40;
  private const int _FONTS = 2;

  /// <summary>
  /// A project of a different shape from the one the encoder writes, drawing a picture the encoder
  /// must nonetheless be able to state: two character sets rather than thirty, characters shared
  /// between cells, and a colour table that changes down every scanline.
  /// </summary>
  /// <remarks>
  /// Different on purpose. The encoder gives every cell a character of its own and a set to every
  /// row, which is one way of writing a picture and not the only one — so a project built that way
  /// would prove only that the encoder agrees with itself.
  /// </remarks>
  private static Graph2FontFile _Handmade() {
    var fontsOffset = 3 + 30 * _COLUMNS;
    var numbers = fontsOffset + _FONTS * Graph2FontFile.FontSize;
    var data = new byte[numbers + 153724];

    data[0] = _COLUMNS;
    data[2] = _FONTS - 1;
    data[numbers + 147679] = 2;

    for (var row = 0; row < 30; ++row) {
      data[numbers + row] = (byte)(row % _FONTS);
      data[numbers + 153694 + row] = 2;

      // No character carries the high bit: it would ask for a fifth colour, and a fifth colour is
      // one byte per cell where every other colour choice is one byte per scanline.
      for (var column = 0; column < _COLUMNS; ++column)
        data[3 + row * _COLUMNS + column] = (byte)((row * 7 + column * 3) % 128);
    }

    for (var at = 0; at < _FONTS * Graph2FontFile.FontSize; ++at)
      data[fontsOffset + at] = (byte)(at * 37 + (at >> 5) * 11);

    for (var y = 0; y < Graph2FontFile.Height; ++y) {
      data[numbers + 30 + y] = (byte)(y / 15 * 2 & 254);
      data[numbers + 30 + y + 256] = (byte)(0x28 | (y & 14));
      data[numbers + 30 + y + 512] = (byte)(0x94 | (y / 3 & 14));
      data[numbers + 30 + y + 768] = (byte)(0xC2 | (y / 7 & 14));
    }

    return Graph2FontReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = Graph2FontFile.ToRawImage(_Handmade());
    var decoded = Graph2FontFile.ToRawImage(Graph2FontFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(Graph2FontFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(Graph2FontFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = Graph2FontFile.ToRawImage(
      Graph2FontFile.FromRawImage(Graph2FontFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(Graph2FontFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(Graph2FontFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheBordersTakeTheBackgroundAndNothingElse() {
    // The quirk: the playfield is 320 of the 336 pixels shown and the eight either side of it are
    // drawn by the background register alone. A picture whose edges disagree with its border cannot
    // have both, so the border wins — and both borders of a scanline are therefore the same colour.
    var source = Graph2FontFile.ToRawImage(_Handmade());
    var rgb = _Rgb(Graph2FontFile.ToRawImage(Graph2FontFile.FromRawImage(source)));

    for (var y = 0; y < Graph2FontFile.Height; ++y) {
      var left = (y * Graph2FontFile.Width) * 3;
      var right = (y * Graph2FontFile.Width + Graph2FontFile.Width - 1) * 3;

      Assert.That(rgb[right..(right + 3)], Is.EqualTo(rgb[left..(left + 3)]), $"scanline {y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = Graph2FontFile.FromRawImage(Graph2FontFile.ToRawImage(_Handmade()));
    var bytes = Graph2FontWriter.ToBytes(file);
    var restored = Graph2FontReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(Graph2FontEncoder.Length));
      Assert.That(_Rgb(Graph2FontFile.ToRawImage(restored)), Is.EqualTo(_Rgb(Graph2FontFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => Graph2FontFile.FromRawImage(null!));
}
