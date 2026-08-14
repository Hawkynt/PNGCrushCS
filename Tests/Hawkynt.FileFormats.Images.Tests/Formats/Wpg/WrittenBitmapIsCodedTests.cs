using System.Linq;
using FileFormat.Core;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

/// <summary>
/// The bitmap in a WPG record is run-length coded, and this library used to write it raw.
/// </summary>
/// <remarks>
/// Our own reader took the raw form back, because it guesses: a payload exactly as long as the
/// raster is treated as uncompressed and anything else is decoded. Nothing outside this library
/// guesses — the record has one coding and XnView's converter applies it — so every byte of a raw
/// raster was read as a control byte. A 32 by 16 greyscale ramp written here and read there came
/// back sharing 71% of its pixels with the one that went in.
/// <para/>
/// With the rows coded the same picture comes back from that converter identical, pixel for pixel,
/// and the file is 124 bytes where it was 350.
/// </remarks>
[TestFixture]
public sealed class WrittenBitmapIsCodedTests {

  /// <summary>
  /// A ramp down the picture, so each row is one value.
  /// </summary>
  /// <remarks>
  /// Deliberately the shape the coding is good at. A ramp across the row instead is the shape it is
  /// worst at — thirty-two different values in a row code as thirty-three bytes of literal, one more
  /// than the row itself — and a test that asserted the file got smaller on that picture would be
  /// asserting something untrue of a correct encoder.
  /// </remarks>
  private static RawImage _Ramp(int width = 32, int height = 16) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(y * 255 / (height - 1));

    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i)
      palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)i;

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>A run of one value is written as a run, not as that many literals.</summary>
  [Test]
  [Category("Unit")]
  public void ARowOfOneValueIsCodedAsARun() {
    var coded = WpgRleCompressor.CompressRows(Enumerable.Repeat((byte)0x2A, 40).ToArray(), 40, 1);

    Assert.That(coded, Has.Length.LessThan(40));
  }

  /// <summary>
  /// A run stops at the end of its row.
  /// </summary>
  /// <remarks>
  /// Two rows of the same value are two runs and not one of twice the length. Coding the raster in
  /// one pass would merge them, and a reader that stops at the end of a row would then be standing
  /// in the middle of a run rather than on a control byte.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ARunDoesNotCarryIntoTheNextRow() {
    var oneRow = WpgRleCompressor.CompressRows(Enumerable.Repeat((byte)0x2A, 8).ToArray(), 8, 1);
    var twoRows = WpgRleCompressor.CompressRows(Enumerable.Repeat((byte)0x2A, 16).ToArray(), 8, 2);

    Assert.That(twoRows, Has.Length.EqualTo(oneRow.Length * 2));
  }

  /// <summary>
  /// The bitmap the writer emits is shorter than the raster, which raw output can never be.
  /// </summary>
  /// <remarks>
  /// Measured on the coded bitmap rather than on the file, because the file also carries a
  /// 768-byte colour map and its records, and those swamp a raster this size either way.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void TheBitmapIsCodedAndNotRaw() {
    var image = _Ramp();
    var coded = WpgRleCompressor.CompressRows(image.PixelData, image.Width, image.Height);

    Assert.That(coded, Has.Length.LessThan(image.PixelData.Length));
  }

  /// <summary>And it still reads back as the picture that went in.</summary>
  [Test]
  [Category("Unit")]
  public void TheWrittenFileStillReadsBackUnchanged() {
    var image = _Ramp();
    var back = WpgFile.ToRawImage(WpgReader.FromBytes(WpgWriter.ToBytes(WpgFile.FromRawImage(image))));

    Assert.Multiple(() => {
      Assert.That(back.Width, Is.EqualTo(image.Width));
      Assert.That(back.Height, Is.EqualTo(image.Height));
      Assert.That(back.PixelData[..image.PixelData.Length], Is.EqualTo(image.PixelData));
    });
  }
}
