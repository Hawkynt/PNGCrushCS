using System;
using System.IO;
using System.Text;
using FileFormat.MetaImage;

namespace FileFormat.MetaImage.Tests;

[TestFixture]
public sealed class MetaImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MetaImageReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MetaImageReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mha"));
    Assert.Throws<FileNotFoundException>(() => MetaImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MetaImageReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => MetaImageReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingObjectType_ThrowsInvalidDataException() {
    var header = "NDims = 2\nDimSize = 2 2\nElementType = MET_UCHAR\nElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 4];
    Array.Copy(headerBytes, data, headerBytes.Length);

    Assert.Throws<InvalidDataException>(() => MetaImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesDimensions() {
    var header =
      "ObjectType = Image\n" +
      "NDims = 2\n" +
      "DimSize = 3 2\n" +
      "ElementType = MET_UCHAR\n" +
      "ElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[3 * 2]; // 3x2 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 40);

    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = MetaImageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ElementType, Is.EqualTo(MetaImageElementType.MetUChar));
    Assert.That(result.Channels, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(6));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesChannels() {
    var header =
      "ObjectType = Image\n" +
      "NDims = 2\n" +
      "DimSize = 2 2\n" +
      "ElementType = MET_UCHAR\n" +
      "ElementNumberOfChannels = 3\n" +
      "ElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[2 * 2 * 3]; // 2x2 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 20);

    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = MetaImageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.PixelData.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var header =
      "ObjectType = Image\n" +
      "NDims = 2\n" +
      "DimSize = 1 1\n" +
      "ElementType = MET_UCHAR\n" +
      "ElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 1];
    Array.Copy(headerBytes, data, headerBytes.Length);
    data[headerBytes.Length] = 0xAB;

    using var stream = new MemoryStream(data);
    var result = MetaImageReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CompressedData_ParsesFlag() {
    var header =
      "ObjectType = Image\n" +
      "NDims = 2\n" +
      "DimSize = 1 1\n" +
      "ElementType = MET_UCHAR\n" +
      "CompressedData = True\n" +
      "ElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);

    // Compress 1 byte with gzip
    byte[] compressed;
    using (var compMs = new MemoryStream()) {
      using (var gzip = new System.IO.Compression.GZipStream(compMs, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        gzip.Write(new byte[] { 0x42 });
      compressed = compMs.ToArray();
    }

    var data = new byte[headerBytes.Length + compressed.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(compressed, 0, data, headerBytes.Length, compressed.Length);

    var result = MetaImageReader.FromBytes(data);

    Assert.That(result.IsCompressed, Is.True);
    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MetShort_ParsesElementType() {
    var header =
      "ObjectType = Image\n" +
      "NDims = 2\n" +
      "DimSize = 1 1\n" +
      "ElementType = MET_SHORT\n" +
      "ElementDataFile = LOCAL\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 2]; // 1 pixel, 2 bytes
    Array.Copy(headerBytes, data, headerBytes.Length);
    data[headerBytes.Length] = 0x01;
    data[headerBytes.Length + 1] = 0x02;

    var result = MetaImageReader.FromBytes(data);

    Assert.That(result.ElementType, Is.EqualTo(MetaImageElementType.MetShort));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
  }
}
