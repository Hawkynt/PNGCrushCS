using System;
using System.IO;
using FileFormat.Commodore64Font;
using FileFormat.Core;

namespace FileFormat.Commodore64Font.Tests;

[TestFixture]
public sealed class Commodore64FontTests {

  private static byte[] _File(int length, byte low, byte high) {
    var data = new byte[length];
    data[0] = low;
    data[1] = high;

    return data;
  }

  [Test]
  public void SeuckSetsAreRecognisedByTheirLoadAddress() {
    Assert.Multiple(() => {
      Assert.That(Commodore64FontReader.FromBytes(_File(514, 66, 0)).Kind, Is.EqualTo(Commodore64FontKind.SeuckFont));
      Assert.That(Commodore64FontReader.FromBytes(_File(2050, 0, 8)).Kind, Is.EqualTo(Commodore64FontKind.CharacterSet));
      // A 514-byte file that does not load at $0042 is an ordinary set, not a SEUCK one.
      Assert.That(Commodore64FontReader.FromBytes(_File(514, 0, 8)).Kind, Is.EqualTo(Commodore64FontKind.CharacterSet));
    });
  }

  [Test]
  public void Height_IsAWholeNumberOfGlyphRows() {
    Assert.Multiple(() => {
      Assert.That(Commodore64FontFile.HeightFor(2050), Is.EqualTo(64), "256 glyphs");
      Assert.That(Commodore64FontFile.HeightFor(514), Is.EqualTo(16), "64 glyphs");
      Assert.That(Commodore64FontFile.HeightFor(1026), Is.EqualTo(32), "128 glyphs");
    });
  }

  [Test]
  public void AGlyphsBytesAreConsecutiveInTheFileButVerticalOnScreen() {
    var data = _File(2050, 0, 8);
    // The eight bytes of glyph 0 are the eight scanlines of the leftmost 8x8 cell.
    for (var row = 0; row < 8; ++row)
      data[Commodore64FontFile.HeaderSize + row] = (byte)(row % 2 == 0 ? 0xFF : 0x00);

    var image = Commodore64FontFile.ToRawImage(Commodore64FontReader.FromBytes(data));

    Assert.Multiple(() => {
      for (var y = 0; y < 8; ++y)
        Assert.That(image.PixelData[y * Commodore64FontFile.Width], Is.EqualTo(y % 2 == 0 ? 1 : 0), $"row {y}");

      // Glyph 1 starts eight bytes later and eight pixels to the right, not on the next scanline.
      Assert.That(image.PixelData[8], Is.Zero);
    });
  }

  [Test]
  public void GlyphRows_StartEveryThirtyTwoGlyphs() {
    var data = _File(2050, 0, 8);
    // The first byte of the second row of glyphs.
    data[Commodore64FontFile.HeaderSize + Commodore64FontFile.BytesPerGlyphRow] = 0x80;

    var image = Commodore64FontFile.ToRawImage(Commodore64FontReader.FromBytes(data));

    Assert.That(image.PixelData[8 * Commodore64FontFile.Width], Is.EqualTo(1));
  }

  [Test]
  public void Bits_RunMostSignificantFirst() {
    var data = _File(2050, 0, 8);
    data[Commodore64FontFile.HeaderSize] = 0b1000_0001;

    var image = Commodore64FontFile.ToRawImage(Commodore64FontReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(1));
      Assert.That(image.PixelData[7], Is.EqualTo(1));
      Assert.That(image.PixelData[1], Is.Zero);
    });
  }

  [Test]
  public void ItIsShownWhiteOnBlack_BecauseTheFileHoldsNoColors() {
    var image = Commodore64FontFile.ToRawImage(Commodore64FontReader.FromBytes(_File(2050, 0, 8)));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.Palette![..3], Is.EqualTo(new byte[] { 0, 0, 0 }));
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 255, 255, 255 }));
    });
  }

  [Test]
  public void RoundTrip_PreservesGlyphsAndLoadAddress() {
    var data = _File(514, 66, 0);
    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i * 17);

    var written = Commodore64FontWriter.ToBytes(Commodore64FontReader.FromBytes(data));

    Assert.That(written, Is.EqualTo(data));
  }

  [Test]
  public void EncodingThenDecoding_ReproducesTheGlyphs() {
    var data = _File(2050, 0, 8);
    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i * 29);

    var source = Commodore64FontFile.ToRawImage(Commodore64FontReader.FromBytes(data));
    var again = Commodore64FontFile.FromRawImage(PixelConverter.Convert(source, PixelFormat.Rgb24));

    Assert.That(again.GlyphData, Is.EqualTo(data[Commodore64FontFile.HeaderSize..]));
  }

  [Test]
  public void Reader_RejectsALoadAddressOffAPageBoundary() {
    Assert.Throws<InvalidDataException>(() => Commodore64FontReader.FromBytes(_File(2050, 3, 8)));
  }

  [Test]
  public void Reader_RejectsSizesOutsideTheRange() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => Commodore64FontReader.FromBytes(_File(9, 0, 8)));
      Assert.Throws<InvalidDataException>(() => Commodore64FontReader.FromBytes(_File(2051, 0, 8)));
    });
  }

  [Test]
  public void FromRawImage_RejectsAPartialGlyphRow() {
    var image = new RawImage {
      Width = Commodore64FontFile.Width, Height = 12,
      Format = PixelFormat.Rgb24, PixelData = new byte[Commodore64FontFile.Width * 12 * 3],
    };

    Assert.Throws<ArgumentException>(() => Commodore64FontFile.FromRawImage(image));
  }
}
