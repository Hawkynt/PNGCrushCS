using System;
using System.IO;
using FileFormat.PsionPic;
using FileFormat.Core;

namespace FileFormat.PsionPic.Tests;

/// <summary>
/// What a Psion PIC file is.
/// </summary>
/// <remarks>
/// These used to hand the reader a buffer with a size written into bytes 0 to 5 and assert that
/// whatever came back had a positive size. A real file opens with "PIC" 0xDC "00", counts its
/// bitmaps, and gives each a twelve-byte record; none of that was checked, and the extension the
/// reader claimed — .ppic — is one no Psion file carries.
/// </remarks>
[TestFixture]
public class PsionPicReaderTests {

  /// <summary>Builds a file of one bitmap, rows padded out to whole sixteen-bit words.</summary>
  private static byte[] _BuildValidFile(int width, int height, byte fill) {
    var bytesPerRow = (width + 15) / 16 * 2;
    var size = bytesPerRow * height;

    using var ms = new MemoryStream();
    foreach (var b in PsionPicFile.Magic)
      ms.WriteByte(b);

    ms.WriteByte(1);                                    // one bitmap
    ms.WriteByte(0);

    void Word(int value) {
      ms.WriteByte((byte)value);
      ms.WriteByte((byte)(value >> 8));
    }

    Word(0);                                            // checksum, which nothing checks
    Word(width);
    Word(height);
    Word(size);
    Word(0);                                            // offset from the end of this record
    Word(0);

    for (var i = 0; i < size; ++i)
      ms.WriteByte(fill);

    return ms.ToArray();
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => PsionPicReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PsionPicReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_WithoutTheSignature_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PsionPicReader.FromBytes(new byte[64]));

  [Test]
  public void FromBytes_TakesItsSizeFromTheBitmapRecord() {
    var result = PsionPicReader.FromBytes(_BuildValidFile(38, 34, 0x00));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(38));
      Assert.That(result.Height, Is.EqualTo(34));
      Assert.That(result.Count, Is.EqualTo(1));
      Assert.That(result.PixelData.Length, Is.EqualTo(38 * 34));
    });
  }

  [Test]
  public void FromBytes_ReadsTheBitsFromTheLeastSignificantEnd() {
    // 0x01 sets the first pixel of each group of eight and nothing else.
    var result = PsionPicReader.FromBytes(_BuildValidFile(16, 2, 0x01));

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(1));
      Assert.That(result.PixelData[1], Is.EqualTo(0));
      Assert.That(result.PixelData[8], Is.EqualTo(1));
    });
  }

  [Test]
  public void FromBytes_RowsArePaddedToWholeWords() {
    // 38 pixels need five bytes but take six, so the second row starts six bytes in rather than five.
    var data = _BuildValidFile(38, 2, 0x00);
    data[^6] = 0x01;

    var result = PsionPicReader.FromBytes(data);

    Assert.That(result.PixelData[38], Is.EqualTo(1), "the first pixel of the second row");
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PsionPicReader.FromStream(null!));

  [Test]
  public void ToRawImage_IsIndexedBlackOnWhite() {
    var raw = PsionPicFile.ToRawImage(PsionPicReader.FromBytes(_BuildValidFile(16, 2, 0x00)));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(2));
      Assert.That(raw.Palette[..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
    });
  }
}
