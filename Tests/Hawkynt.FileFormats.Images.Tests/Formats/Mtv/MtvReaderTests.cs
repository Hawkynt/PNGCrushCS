using System;
using System.IO;
using System.Text;
using FileFormat.Mtv;

namespace FileFormat.Mtv.Tests;

[TestFixture]
public sealed class MtvReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MtvReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MtvReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mtv"));
    Assert.Throws<FileNotFoundException>(() => MtvReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MtvReader.FromStream(null!));

  [TestCase("2 2")]
  [TestCase("2\n")]
  [TestCase("0 1\n")]
  [TestCase("1 0\n")]
  [TestCase("-1 1\n")]
  [TestCase("1 1 junk\n")]
  [TestCase("100000 100000\n")]
  [TestCase("2147483648 1\n")]
  [Category("Unit")]
  public void FromBytes_MalformedHeader_ThrowsInvalidDataException(string header) {
    var data = _File(header, [0, 0, 0]);
    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_KnownVector_DecodesRgbTriplesInOrder() {
    var data = _File("2 2\n", [
      255, 0, 0,
      0, 255, 0,
      0, 0, 255,
      17, 34, 51,
    ]);

    var file = MtvReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.PixelData, Is.EqualTo(new byte[] {
        255, 0, 0,
        0, 255, 0,
        0, 0, 255,
        17, 34, 51,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CrlfAndScanfWhitespace_AreAccepted() {
    var data = _File(" \t+2  1 \r\n", [1, 2, 3, 4, 5, 6]);

    var file = MtvReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedRaster_ThrowsInvalidDataException() {
    var data = _File("2 1\n", [1, 2, 3, 4, 5]);

    var exception = Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));

    Assert.That(exception!.Message, Does.Contain("needs 6"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NconvertZeroPadding_IsSkipped() {
    var data = _File("1 1\n", [0, 42, 84, 126]);

    var file = MtvReader.FromBytes(data);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 42, 84, 126 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactRasterBeginningWithZero_IsNotTreatedAsPadding() {
    var data = _File("1 1\n", [0, 84, 126]);

    var file = MtvReader.FromBytes(data);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0, 84, 126 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TrailingData_IsIgnoredLikeHistoricalDecoder() {
    var data = _File("1 1\n", [10, 20, 30, 99, 98, 97]);

    var file = MtvReader.FromBytes(data);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesFromCurrentPosition() {
    var encoded = _File("1 1\n", [11, 22, 33]);
    using var stream = new MemoryStream([0xFF, .. encoded]);
    stream.Position = 1;

    var file = MtvReader.FromStream(stream);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 11, 22, 33 }));
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_HeaderOnly_ReturnsDimensionsAndRgb24() {
    var info = MtvFile.ReadImageInfo("320 200\n"u8);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(320));
      Assert.That(info.Value.Height, Is.EqualTo(200));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(24));
      Assert.That(info.Value.ColorMode, Is.EqualTo("Rgb24"));
    });
  }

  private static byte[] _File(string header, byte[] raster) {
    var prefix = Encoding.ASCII.GetBytes(header);
    var result = new byte[prefix.Length + raster.Length];
    prefix.CopyTo(result, 0);
    raster.CopyTo(result, prefix.Length);
    return result;
  }
}
