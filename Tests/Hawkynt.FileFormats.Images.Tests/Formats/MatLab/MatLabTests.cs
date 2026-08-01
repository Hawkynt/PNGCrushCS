using System;
using System.IO;
using FileFormat.MatLab;
using FileFormat.Core;

namespace FileFormat.MatLab.Tests;

[TestFixture]
public class MatLabReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MatLabReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MatLabReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MatLabReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MatLabReader.FromBytes(new byte[127]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    // Built through the writer rather than by hand. Nothing about the picture sits at a fixed
    // offset here — the file is a chain of tagged elements — so a hand-built fixture can only agree
    // with a reader that invented the same layout, which is what both used to do.
    var written = MatLabWriter.ToBytes(new() { Width = 64, Height = 64, PixelData = new byte[64 * 64 * 3] });

    var result = MatLabReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(64));
    });
  }

  [Test]
  public void FromBytes_NoByteOrderMark_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MatLabReader.FromBytes(new byte[256]));

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MatLabReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new MatLabFile {
      Width = 64,
      Height = 64,
      PixelData = new byte[64 * 64 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = MatLabWriter.ToBytes(file);
    var file2 = MatLabReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new MatLabFile {
      Width = 64,
      Height = 64,
      PixelData = new byte[64 * 64 * 3],
    };
    var raw = MatLabFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = MatLabFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

