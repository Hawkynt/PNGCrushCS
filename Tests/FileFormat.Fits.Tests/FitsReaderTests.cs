using System;
using System.IO;
using System.Text;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class FitsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FitsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FitsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fits"));
    Assert.Throws<FileNotFoundException>(() => FitsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FitsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => FitsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[2880];
    Encoding.ASCII.GetBytes("INVALID ").CopyTo(bad, 0);
    Assert.Throws<InvalidDataException>(() => FitsReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUInt8_ParsesCorrectly() {
    var data = _BuildMinimalFits(4, 3, FitsBitpix.UInt8);
    var result = FitsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Bitpix, Is.EqualTo(FitsBitpix.UInt8));
    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3));
  }

  private static byte[] _BuildMinimalFits(int width, int height, FitsBitpix bitpix) {
    var cards = new[] {
      _MakeCard("SIMPLE", "T"),
      _MakeCard("BITPIX", ((int)bitpix).ToString()),
      _MakeCard("NAXIS", "2"),
      _MakeCard("NAXIS1", width.ToString()),
      _MakeCard("NAXIS2", height.ToString()),
      "END".PadRight(80)
    };

    var headerSize = 2880;
    var bytesPerPixel = Math.Abs((int)bitpix) / 8;
    var dataSize = width * height * bytesPerPixel;
    var paddedDataSize = dataSize > 0 ? ((dataSize + 2879) / 2880) * 2880 : 0;
    var totalSize = headerSize + paddedDataSize;

    var data = new byte[totalSize];
    // Fill header block with spaces
    for (var i = 0; i < headerSize; ++i)
      data[i] = (byte)' ';

    var offset = 0;
    foreach (var card in cards) {
      var cardBytes = Encoding.ASCII.GetBytes(card);
      Array.Copy(cardBytes, 0, data, offset, Math.Min(cardBytes.Length, 80));
      offset += 80;
    }

    // Fill pixel data with a pattern
    for (var i = headerSize; i < headerSize + dataSize; ++i)
      data[i] = (byte)((i - headerSize) % 256);

    return data;
  }

  private static string _MakeCard(string name, string value)
    => $"{name,-8}= {value,20}".PadRight(80);
}
