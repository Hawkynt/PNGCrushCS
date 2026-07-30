using System;
using System.IO;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class HeifReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".heic"));
    Assert.Throws<FileNotFoundException>(() => HeifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => HeifReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    // Build a valid-size ISOBMFF box with wrong type
    var data = _BuildFtypBox("ABCD");
    Assert.Throws<InvalidDataException>(() => HeifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoFtypBox_ThrowsInvalidDataException() {
    // Build a box that is not ftyp
    var data = new byte[16];
    _WriteUint32BE(data, 0, 16);
    System.Text.Encoding.ASCII.GetBytes("mdat", 0, 4, data, 4);
    Assert.Throws<InvalidDataException>(() => HeifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeic_ParsesDimensions() {
    var heifBytes = _BuildMinimalHeifFile("heic", 320, 240, new byte[320 * 240 * 3]);
    var result = HeifReader.FromBytes(heifBytes);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeic_ParsesBrand() {
    var heifBytes = _BuildMinimalHeifFile("heic", 4, 4, new byte[48]);
    var result = HeifReader.FromBytes(heifBytes);

    Assert.That(result.Brand, Is.EqualTo("heic"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Mif1Brand_Accepted() {
    var heifBytes = _BuildMinimalHeifFile("mif1", 2, 2, new byte[12]);
    var result = HeifReader.FromBytes(heifBytes);

    Assert.That(result.Brand, Is.EqualTo("mif1"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HevcBrand_Accepted() {
    var heifBytes = _BuildMinimalHeifFile("hevc", 2, 2, new byte[12]);
    var result = HeifReader.FromBytes(heifBytes);

    Assert.That(result.Brand, Is.EqualTo("hevc"));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidHeic_ParsesDimensions() {
    var heifBytes = _BuildMinimalHeifFile("heic", 16, 8, new byte[16 * 8 * 3]);
    using var ms = new MemoryStream(heifBytes);
    var result = HeifReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawImageData_Extracted() {
    var imageData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    var heifBytes = _BuildMinimalHeifFile("heic", 2, 1, imageData);
    var result = HeifReader.FromBytes(heifBytes);

    Assert.That(result.RawImageData, Is.EqualTo(imageData));
  }

  // --- Helpers ---

  private static byte[] _BuildFtypBox(string brand) {
    var data = new byte[24];
    _WriteUint32BE(data, 0, 24);
    System.Text.Encoding.ASCII.GetBytes("ftyp", 0, 4, data, 4);
    System.Text.Encoding.ASCII.GetBytes(brand, 0, 4, data, 8);
    // minor version = 0 (12..15)
    System.Text.Encoding.ASCII.GetBytes(brand, 0, 4, data, 16);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, data, 20);
    return data;
  }

  private static byte[] _BuildMinimalHeifFile(string brand, int width, int height, byte[] imageData) {
    // Use the writer to build a valid file then patch the brand if needed
    var file = new HeifFile {
      Width = width,
      Height = height,
      PixelData = imageData,
      RawImageData = imageData,
      Brand = brand,
    };
    return HeifWriter.ToBytes(file);
  }

  private static void _WriteUint32BE(byte[] data, int offset, uint value) {
    data[offset] = (byte)(value >> 24);
    data[offset + 1] = (byte)(value >> 16);
    data[offset + 2] = (byte)(value >> 8);
    data[offset + 3] = (byte)value;
  }
}
