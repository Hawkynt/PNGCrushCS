using System;
using System.IO;
using FileFormat.KofaxKfx;
using FileFormat.Core;

namespace FileFormat.KofaxKfx.Tests;

[TestFixture]
public class KofaxKfxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => KofaxKfxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KofaxKfxReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    // The file is the bitmap: seven bytes a row, and the height follows from how many there are.
    var result = KofaxKfxReader.FromBytes(new byte[7 * 60]);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(56));
      Assert.That(result.Height, Is.EqualTo(60));
    });
  }

  [Test]
  public void FromBytes_RefusesALengthThatIsNotWholeRows()
    => Assert.Throws<InvalidDataException>(() => KofaxKfxReader.FromBytes(new byte[7 * 60 + 3]));

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KofaxKfxReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new KofaxKfxFile {
      Width = 56,
      Height = 60,
      PixelData = new byte[7 * 60],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = KofaxKfxWriter.ToBytes(file);
    var file2 = KofaxKfxReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new KofaxKfxFile {
      Width = 56,
      Height = 60,
      PixelData = new byte[7 * 60],
    };
    var raw = KofaxKfxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = KofaxKfxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

