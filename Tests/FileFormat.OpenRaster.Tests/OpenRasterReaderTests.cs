using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.OpenRaster;

namespace FileFormat.OpenRaster.Tests;

[TestFixture]
public sealed class OpenRasterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OpenRasterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OpenRasterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ora"));
    Assert.Throws<FileNotFoundException>(() => OpenRasterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => OpenRasterReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMimetype_ThrowsInvalidDataException() {
    var zipBytes = _BuildZipWithMimetype("text/plain");
    Assert.Throws<InvalidDataException>(() => OpenRasterReader.FromBytes(zipBytes));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OpenRasterReader.FromStream(null!));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidSingleLayer_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < rgba.Length; ++i)
      rgba[i] = (byte)(i * 17 % 256);

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = rgba,
      Layers = [
        new OpenRasterLayer {
          Name = "Background",
          X = 0,
          Y = 0,
          Width = width,
          Height = height,
          Opacity = 1.0f,
          Visibility = true,
          PixelData = rgba
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var result = OpenRasterReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Layers, Has.Count.EqualTo(1));
    Assert.That(result.Layers[0].Name, Is.EqualTo("Background"));
    Assert.That(result.Layers[0].Width, Is.EqualTo(width));
    Assert.That(result.Layers[0].Height, Is.EqualTo(height));
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
