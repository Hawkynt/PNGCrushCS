using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

/// <summary>What a WPG this library writes actually contains.</summary>
/// <remarks>
/// A type 1 bitmap record is run-length coded — that is what the record type means, and there is no
/// flag to say otherwise — but the rows were written raw. Nothing here noticed, because the reader
/// guessed which it had from the length and accepted both; every other reader answers "unable to
/// decompress". The run coding is per scanline, so a run never carries into the next row.
/// </remarks>
[TestFixture]
public sealed class WpgWrittenBitmapTests {

  private const int _Width = 64;

  private static RawImage Striped() {
    // Half a row of index 0, half of index 1, over two rows: two runs a row, which is what run coding
    // is for and what a raw row cannot match.
    var pixels = new byte[_Width * 2];
    for (var y = 0; y < 2; ++y)
      for (var x = 0; x < _Width; ++x)
        pixels[(y * _Width) + x] = (byte)(x < _Width / 2 ? 0 : 1);

    return new() {
      Width = _Width,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteCount = 2,
    };
  }

  [Test]
  [Category("Unit")]
  public void The_header_says_where_the_records_start_and_what_the_file_is() {
    var bytes = WpgWriter.ToBytes(WpgFile.FromRawImage(Striped()));

    Assert.Multiple(() => {
      Assert.That(bytes[..4], Is.EqualTo(new byte[] { 0xFF, (byte)'W', (byte)'P', (byte)'C' }));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(16u),
        "the records begin after the header, not at byte 1");
      Assert.That(bytes[9], Is.EqualTo(WpgHeader.GraphicFileType), "the file says it is a graphic");
    });
  }

  [Test]
  [Category("Unit")]
  public void The_file_carries_the_coded_rows_and_not_the_raw_ones() {
    var file = WpgFile.FromRawImage(Striped());
    var stride = ((file.Width * file.BitsPerPixel) + 7) / 8;
    var coded = WpgRleCompressor.CompressRows(file.PixelData, stride, file.Height);
    var bytes = WpgWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(coded, Has.Length.LessThan(file.PixelData.Length), "two runs a row code shorter than the row");
      Assert.That(_Contains(bytes, coded), Is.True, "the coded rows are in the file");
      Assert.That(_Contains(bytes, file.PixelData), Is.False, "and the raw ones are not");
    });
  }

  /// <summary>Whether <paramref name="haystack"/> holds <paramref name="needle"/> end to end.</summary>
  private static bool _Contains(byte[] haystack, byte[] needle) {
    for (var at = 0; at + needle.Length <= haystack.Length; ++at) {
      var same = true;
      for (var i = 0; i < needle.Length && same; ++i)
        same = haystack[at + i] == needle[i];

      if (same)
        return true;
    }

    return false;
  }

  [Test]
  [Category("Unit")]
  public void Coding_a_row_at_a_time_keeps_the_rows_apart() {
    // One row of a single value, then a row of another: coded together the two would merge into one
    // run and every row after the first would start in the wrong place.
    var pixels = new byte[8 * 2];
    for (var x = 0; x < 8; ++x)
      pixels[8 + x] = 1;

    var coded = WpgRleCompressor.CompressRows(pixels, 8, 2);
    var back = WpgRleCompressor.Decompress(coded, pixels.Length);

    Assert.That(back, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsThePixels() {
    var original = Striped();
    var back = WpgFile.ToRawImage(WpgReader.FromBytes(WpgWriter.ToBytes(WpgFile.FromRawImage(original)))).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(back[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "the left half");
      var right = (_Width / 2) * 3;
      Assert.That(back[right..(right + 3)], Is.EqualTo(new byte[] { 0, 255, 0 }), "the right half");
    });
  }
}
