using System;
using System.IO;
using FileFormat.WinFax;
using FileFormat.Core;

namespace FileFormat.WinFax.Tests;

[TestFixture]
public class WinFaxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WinFaxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => WinFaxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WinFaxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WinFaxReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    // Built by the writer, since a fax states its page coded and a block of zeros is not one.
    var page = new WinFaxFile { Width = 1728, Height = 16, PixelData = new byte[1728 / 8 * 16] };
    var result = WinFaxReader.FromBytes(WinFaxWriter.ToBytes(page));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(1728));
      Assert.That(result.Height, Is.EqualTo(16));
    });
  }

  [Test]
  public void FromBytes_RefusesSomethingWithoutTheSignature()
    => Assert.Throws<InvalidDataException>(() => WinFaxReader.FromBytes(new byte[64]));

  [Test]
  public void FromBytes_TakesTheSizeFromTheFieldsThatHoldIt() {
    // They were read from offsets 0 and 4, which hold the signature and half the height, and a field
    // that came out wrong was replaced with a default rather than refusing the file.
    var page = new WinFaxFile { Width = 1728, Height = 32, PixelData = new byte[1728 / 8 * 32] };
    var bytes = WinFaxWriter.ToBytes(page);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0x0B));
      Assert.That(bytes[1], Is.EqualTo(0x23));
      Assert.That(bytes[3] | (bytes[4] << 8), Is.EqualTo(1728));
      Assert.That(bytes[5] | (bytes[6] << 8), Is.EqualTo(32));
    });
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WinFaxReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new WinFaxFile {
      Width = 1728,
      Height = 64,
      PixelData = new byte[1728 / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);

    var restored = WinFaxReader.FromBytes(WinFaxWriter.ToBytes(file));

    Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new WinFaxFile {
      Width = 1728,
      Height = 64,
      PixelData = new byte[1728 / 8 * 64],
    };
    var raw = WinFaxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = WinFaxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

