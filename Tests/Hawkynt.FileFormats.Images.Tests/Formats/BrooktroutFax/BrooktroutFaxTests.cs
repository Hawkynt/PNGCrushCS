using System;
using System.IO;
using FileFormat.BrooktroutFax;
using FileFormat.Core;

namespace FileFormat.BrooktroutFax.Tests;

[TestFixture]
public class BrooktroutFaxReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => BrooktroutFaxReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BrooktroutFaxReader.FromBytes(new byte[31]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    // Built by the writer, since a fax states its page coded and a block of zeros is not one.
    var page = new BrooktroutFaxFile { Width = 1728, Height = 16, PixelData = new byte[1728 / 8 * 16] };
    var result = BrooktroutFaxReader.FromBytes(BrooktroutFaxWriter.ToBytes(page));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(1728));
      Assert.That(result.Height, Is.EqualTo(16));
    });
  }

  [Test]
  public void FromBytes_RefusesSomethingWithoutTheSignature()
    => Assert.Throws<InvalidDataException>(() => BrooktroutFaxReader.FromBytes(new byte[256]));

  [Test]
  public void FromBytes_TakesTheSizeFromTheFieldsThatHoldIt() {
    // They were read from offsets 0 and 4 — the signature and the resolution — and a field that came
    // out wrong was replaced with a default rather than refusing the file.
    var bytes = BrooktroutFaxWriter.ToBytes(
      new BrooktroutFaxFile { Width = 1728, Height = 32, PixelData = new byte[1728 / 8 * 32] });

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xBB));
      Assert.That(bytes[1], Is.EqualTo(0x01));
      Assert.That(bytes[9] | (bytes[10] << 8), Is.EqualTo(1728));
      Assert.That(bytes[45] | (bytes[46] << 8), Is.EqualTo(32));
    });
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BrooktroutFaxReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new BrooktroutFaxFile {
      Width = 1728,
      Height = 64,
      PixelData = new byte[1728 / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = BrooktroutFaxWriter.ToBytes(file);
    var file2 = BrooktroutFaxReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new BrooktroutFaxFile {
      Width = 1728,
      Height = 64,
      PixelData = new byte[1728 / 8 * 64],
    };
    var raw = BrooktroutFaxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = BrooktroutFaxFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

