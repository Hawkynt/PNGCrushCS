using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EpaBios.Tests;

/// <summary>
/// A BIOS logo as the BIOS stores it: cell counts, an attribute a cell, and a glyph a cell.
/// </summary>
[TestFixture]
public sealed class EpaBiosTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x < width / 2 ? 0 : 255);
      pixels[at + 1] = (byte)(y < height / 2 ? 0 : 255);
      pixels[at + 2] = 170;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_StatesItsCellCountsAndIsAsLongAsTheyMake() {
    var bytes = EpaBiosWriter.ToBytes(EpaBiosFile.FromRawImage(_Picture(136, 84)));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(17), "136 pixels is seventeen eight-wide cells");
      Assert.That(bytes[1], Is.EqualTo(6), "84 pixels is six fourteen-high cells");
      Assert.That(bytes, Has.Length.EqualTo(2 + 17 * 6 * 15 + 70));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheSizeFromTheCellCountsRatherThanAssumingOne() {
    var bytes = new byte[2 + 4 * 3 * 15 + 70];
    bytes[0] = 4;
    bytes[1] = 3;

    var file = EpaBiosReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(32));
      Assert.That(file.Height, Is.EqualTo(42));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesALengthTheCellCountsDoNotAccountFor() {
    var bytes = new byte[2 + 4 * 3 * 15 + 70 + 1];
    bytes[0] = 4;
    bytes[1] = 3;

    Assert.Throws<InvalidDataException>(() => EpaBiosReader.FromBytes(bytes));
  }

  [TestCase((byte)0, (byte)6)]
  [TestCase((byte)17, (byte)0)]
  [TestCase((byte)81, (byte)6)]
  [TestCase((byte)17, (byte)26)]
  [Category("Unit")]
  public void Read_RefusesCellCountsNoTextScreenHas(byte columns, byte rows) {
    var bytes = new byte[2 + columns * rows * 15 + 70];
    bytes[0] = columns;
    bytes[1] = rows;

    Assert.Throws<InvalidDataException>(() => EpaBiosReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsABitFromTheInkNibbleAndAClearOneFromTheBackground() {
    var bytes = new byte[2 + 15 + 70];
    bytes[0] = 1;
    bytes[1] = 1;
    // Background white, ink red.
    bytes[2] = 0xF4;
    // Top scanline: leftmost pixel set, the rest clear.
    bytes[3] = 0x80;

    var image = EpaBiosFile.ToRawImage(EpaBiosReader.FromBytes(bytes));
    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(4), "a set bit takes the low nibble");
      Assert.That(image.PixelData[1], Is.EqualTo(15), "a clear one takes the high nibble");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheCellsAndTheirAttributes() {
    var original = EpaBiosFile.FromRawImage(_Picture(136, 84));
    var restored = EpaBiosReader.FromBytes(EpaBiosWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Columns, Is.EqualTo(original.Columns));
      Assert.That(restored.Rows, Is.EqualTo(original.Rows));
      Assert.That(restored.Attributes, Is.EqualTo(original.Attributes));
      Assert.That(restored.Glyphs, Is.EqualTo(original.Glyphs));
    });
  }

  [Test]
  [Category("Integration")]
  public void Written_KeepsEachCellToTheTwoColoursItCanHold() {
    var image = EpaBiosFile.ToRawImage(EpaBiosFile.FromRawImage(_Picture(136, 84)));

    for (var row = 0; row < 6; ++row)
    for (var column = 0; column < 17; ++column) {
      var seen = new System.Collections.Generic.HashSet<byte>();
      for (var y = 0; y < 14; ++y)
      for (var x = 0; x < 8; ++x)
        seen.Add(image.PixelData[(row * 14 + y) * 136 + column * 8 + x]);

      Assert.That(seen, Has.Count.LessThanOrEqualTo(2), $"cell {column},{row}");
    }
  }
}
