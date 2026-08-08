using System;
using FileFormat.Core;

namespace FileFormat.PalmImageViewer.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A whole number of sixteen pixels, which is the only width the record can state.</summary>
  private const int _WIDTH = 48;

  /// <summary>Not a multiple of anything, so a row count assumption shows up.</summary>
  private const int _HEIGHT = 11;

  /// <summary>Greys drawn from the sixteen-step ramp, so nothing has to be rounded to reach it.</summary>
  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var value = (byte)(i % 16 * 17);
      data[i * 3] = data[i * 3 + 1] = data[i * 3 + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenGreys_ReproducesExactly() {
    var source = _Ramp(_WIDTH, _HEIGHT);
    var file = PalmImageViewerFile.FromRawImage(source);
    var decoded = PalmImageViewerFile.ToRawImage(
      PalmImageViewerReader.FromBytes(PalmImageViewerWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(file.BitsPerPixel, Is.EqualTo(PalmImageViewerFile.WrittenBitsPerPixel));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAWidthThatIsNotAWholeNumberOfSixteen() {
    // The program rounded the width up and stored the rounded number, so a picture 100 across is a
    // picture 112 across as far as the record is concerned and every reader shows it that way.
    var wide = PalmImageViewerFile.FromRawImage(_Ramp(100, 37));
    var tall = PalmImageViewerFile.FromRawImage(_Ramp(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((112, 37)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((16, 200)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdentifiesItsRecordTheWayEveryOtherReaderExpects() {
    // A Palm record's identifier is nominally the database's own business and this reader has never
    // looked at it — but the reader every other tool uses compares it against these three bytes and
    // calls the file corrupt when it differs. Zeroes there make a file only we can open.
    var bytes = PalmImageViewerWriter.ToBytes(PalmImageViewerFile.FromRawImage(_Ramp(_WIDTH, _HEIGHT)));

    Assert.Multiple(() => {
      Assert.That(bytes[82], Is.EqualTo(0x40));
      Assert.That(bytes[83..86], Is.EqualTo(new byte[] { 0x6F, 0x80, 0x00 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheDepthRatherThanLeavingItToBeCountedOut() {
    // The record names the depth, and names rather than counts it: two bits is nought and one bit is
    // 255, so a zero left there is not "unset" but a claim that the picture is two bits deep — which
    // is how a four-bit picture comes out as somebody else's stripes.
    var bytes = PalmImageViewerWriter.ToBytes(PalmImageViewerFile.FromRawImage(_Ramp(_WIDTH, _HEIGHT)));

    Assert.Multiple(() => {
      Assert.That(bytes[86 + 33], Is.EqualTo(PalmImageViewerWriter.DepthByte(4)));
      Assert.That(PalmImageViewerWriter.DepthByte(2), Is.EqualTo(0x00));
      Assert.That(PalmImageViewerWriter.DepthByte(1), Is.EqualTo(0xFF));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_TakesTheDepthFromTheRecordRatherThanFromTheRowLength() {
    // A width that is not a multiple of sixteen cannot come from this writer but can come from
    // another, and dividing the row length by it then rounds to the wrong depth. The record states
    // it outright, so there is nothing to work out.
    var file = PalmImageViewerFile.FromRawImage(_Ramp(48, 4));
    var bytes = PalmImageViewerWriter.ToBytes(file);
    bytes[86 + 54] = 0;
    bytes[86 + 55] = 44;

    Assert.That(PalmImageViewerReader.FromBytes(bytes).BitsPerPixel, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void Compress_IsTheExactInverseOfTheReadersDecompression() {
    // Every case the coding has: a run ending exactly on the 128-byte boundary, a longer one that
    // has to be split, single bytes between runs, and a literal run of exactly 128.
    var source = new byte[128 + 300 + 1 + 128 + 3];
    var at = 0;
    for (var i = 0; i < 128; ++i)
      source[at++] = 0xAA;

    for (var i = 0; i < 300; ++i)
      source[at++] = 0x55;

    source[at++] = 0x01;
    for (var i = 0; i < 128; ++i)
      source[at++] = (byte)(i * 7 + 1);

    source[at++] = 0x02;
    source[at++] = 0x03;
    source[at] = 0x04;

    var compressed = PalmImageViewerWriter.Compress(source);

    // Two pixels a byte, so the row is as long as the bytes under test and the record's stated depth
    // is the one they were packed at.
    var file = PalmImageViewerReader.FromBytes(PalmImageViewerWriter.ToBytes(new() {
      Width = source.Length * 2, Height = 1, BitsPerPixel = 4, Name = string.Empty, PixelData = source,
    }));

    Assert.Multiple(() => {
      Assert.That(compressed, Has.Length.LessThan(source.Length));
      Assert.That(file.PixelData, Is.EqualTo(source));
    });
  }
}
