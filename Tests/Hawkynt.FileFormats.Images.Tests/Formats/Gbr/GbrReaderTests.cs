using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Gbr;

namespace FileFormat.Gbr.Tests;

[TestFixture]
public sealed class GbrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GbrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GbrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gbr"));
    Assert.Throws<FileNotFoundException>(() => GbrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GbrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[20];
    Assert.Throws<InvalidDataException>(() => GbrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = _BuildGbrBytes(4, 4, 1, 10, "test", _ReplaceAt: (20, [(byte)'N', (byte)'O', (byte)'P', (byte)'E']));
    Assert.Throws<InvalidDataException>(() => GbrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_ThrowsInvalidDataException() {
    var data = _BuildGbrBytes(4, 4, 1, 10, "test");
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 3u);
    Assert.Throws<InvalidDataException>(() => GbrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBytesPerPixel_ThrowsInvalidDataException() {
    var data = _BuildGbrBytes(4, 4, 1, 10, "test");
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 3u);
    Assert.Throws<InvalidDataException>(() => GbrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var width = 4;
    var height = 2;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31);

    var data = _BuildGbrBytes(width, height, 1, 25, "Gray Brush", pixelData);
    var result = GbrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BytesPerPixel, Is.EqualTo(1));
    Assert.That(result.Spacing, Is.EqualTo(25));
    Assert.That(result.Name, Is.EqualTo("Gray Brush"));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var data = _BuildGbrBytes(width, height, 4, 50, "RGBA Brush", pixelData);
    var result = GbrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.BytesPerPixel, Is.EqualTo(4));
    Assert.That(result.Spacing, Is.EqualTo(50));
    Assert.That(result.Name, Is.EqualTo("RGBA Brush"));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale() {
    var pixelData = new byte[] { 0x00, 0x80, 0xFF, 0x40 };
    var data = _BuildGbrBytes(2, 2, 1, 10, "S", pixelData);

    using var ms = new MemoryStream(data);
    var result = GbrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  private static byte[] _BuildGbrBytes(
    int width, int height, int bpp, int spacing, string name,
    byte[]? pixelData = null,
    (int offset, byte[] bytes)? _ReplaceAt = null
  ) {
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var headerSize = 28 + nameBytes.Length + 1;
    var pixelSize = width * height * bpp;
    pixelData ??= new byte[pixelSize];
    var total = headerSize + pixelSize;
    var result = new byte[total];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)headerSize);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 2u);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)bpp);
    span[20] = (byte)'G';
    span[21] = (byte)'I';
    span[22] = (byte)'M';
    span[23] = (byte)'P';
    BinaryPrimitives.WriteUInt32BigEndian(span[24..], (uint)spacing);
    nameBytes.CopyTo(span[28..]);
    span[28 + nameBytes.Length] = 0;
    Array.Copy(pixelData, 0, result, headerSize, Math.Min(pixelSize, pixelData.Length));

    if (_ReplaceAt.HasValue) {
      var (offset, bytes) = _ReplaceAt.Value;
      bytes.CopyTo(result, offset);
    }

    return result;
  }
}
