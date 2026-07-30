using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Krita;

namespace FileFormat.Krita.Tests;

[TestFixture]
public sealed class KritaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kra"));
    Assert.Throws<FileNotFoundException>(() => KritaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => KritaReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMimetype_ThrowsInvalidDataException() {
    var zipBytes = _BuildZipWithMimetype("text/plain");
    Assert.Throws<InvalidDataException>(() => KritaReader.FromBytes(zipBytes));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaReader.FromStream(null!));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidImage_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < rgba.Length; ++i)
      rgba[i] = (byte)(i * 17 % 256);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var bytes = KritaWriter.ToBytes(original);
    var result = KritaReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height * 4));
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidImage_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < rgba.Length; ++i)
      rgba[i] = (byte)(i * 17 % 256);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var bytes = KritaWriter.ToBytes(original);
    using var stream = new MemoryStream(bytes);
    var result = KritaReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
  }

  private static byte[] _BuildZipWithMimetype(string mimetypeValue) {
    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      var entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
      using var stream = entry.Open();
      var data = Encoding.ASCII.GetBytes(mimetypeValue);
      stream.Write(data, 0, data.Length);
    }

    return ms.ToArray();
  }
}
