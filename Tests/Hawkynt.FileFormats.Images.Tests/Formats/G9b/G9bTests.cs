using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.G9b.Tests;

/// <summary>The V9990 container: a sixteen-byte header, then the palette, then the bitmap.</summary>
[TestFixture]
public sealed class G9bTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i % 256);
      pixels[i + 1] = (byte)((i / 3) % 256);
      pixels[i + 2] = (byte)((i * 7) % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheSixteenByteHeaderTheFormatSpecifies() {
    var bytes = G9bWriter.ToBytes(G9bFile.FromRawImage(_Picture(32, 20)));

    Assert.Multiple(() => {
      Assert.That(bytes[0..3], Is.EqualTo(new byte[] { 0x47, 0x39, 0x42 }));
      Assert.That(bytes[3], Is.EqualTo(11), "the version byte every file carries");
      Assert.That(bytes[4], Is.EqualTo(0));
      Assert.That(bytes[5], Is.EqualTo(16), "the depth, not a screen-mode number");
      Assert.That(bytes[7], Is.EqualTo(0), "sixteen bits a pixel needs no palette");
      Assert.That(bytes[8] | (bytes[9] << 8), Is.EqualTo(32));
      Assert.That(bytes[10] | (bytes[11] << 8), Is.EqualTo(20));
      Assert.That(bytes[12], Is.EqualTo(0), "stored, not packed");
      Assert.That(bytes, Has.Length.EqualTo(16 + 32 * 20 * 2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Direct_PutsGreenInTheTopBits() {
    var bytes = new byte[16 + 2];
    bytes[0] = 0x47; bytes[1] = 0x39; bytes[2] = 0x42; bytes[3] = 11;
    bytes[5] = 16;
    bytes[8] = 1;
    bytes[10] = 1;
    // Green full, red and blue empty: 31 << 10.
    bytes[16] = 0x00;
    bytes[17] = 0x7C;

    var image = G9bFile.ToRawImage(G9bReader.FromBytes(bytes));
    Assert.That(
      (image.PixelData[0], image.PixelData[1], image.PixelData[2]),
      Is.EqualTo(((byte)0, (byte)255, (byte)0)));
  }

  [Test]
  [Category("Unit")]
  public void Indexed_TakesItsColoursFromTheFivBitPaletteThatFollowsTheHeader() {
    var bytes = new byte[16 + 16 * 3 + 1];
    bytes[0] = 0x47; bytes[1] = 0x39; bytes[2] = 0x42; bytes[3] = 11;
    bytes[5] = 4;
    bytes[7] = 16;
    bytes[8] = 2;
    bytes[10] = 1;
    // Entry one is full red at five bits.
    bytes[16 + 3] = 31;
    bytes[16 + 48] = 0x10;

    var image = G9bFile.ToRawImage(G9bReader.FromBytes(bytes));
    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(1), "the high nibble is the leftmost pixel");
      Assert.That(image.PixelData[1], Is.EqualTo(0));
      Assert.That(image.Palette![3], Is.EqualTo(255), "five bits widen by repetition, not by shifting");
    });
  }

  [TestCase((byte)1)]
  [TestCase((byte)3)]
  [TestCase((byte)32)]
  [Category("Unit")]
  public void Read_RefusesADepthTheChipDoesNotHave(byte depth) {
    var bytes = new byte[64];
    bytes[0] = 0x47; bytes[1] = 0x39; bytes[2] = 0x42; bytes[3] = 11;
    bytes[5] = depth;
    bytes[8] = 1;
    bytes[10] = 1;

    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAPackedBitmapRatherThanReadingItAsPixels() {
    var bytes = new byte[16 + 100];
    bytes[0] = 0x47; bytes[1] = 0x39; bytes[2] = 0x42; bytes[3] = 11;
    bytes[5] = 16;
    bytes[8] = 5;
    bytes[10] = 5;
    bytes[12] = 1;

    Assert.Throws<InvalidDataException>(() => G9bReader.FromBytes(bytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryColourTheFiveBitRangeHolds() {
    var file = G9bFile.FromRawImage(_Picture(40, 25));
    var restored = G9bReader.FromBytes(G9bWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(40));
      Assert.That(restored.Height, Is.EqualTo(25));
      Assert.That(restored.Depth, Is.EqualTo(16));
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
    });
  }
}
