using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class HeifWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFtypBox() {
    var file = new HeifFile { Width = 2, Height = 2, PixelData = new byte[12] };
    var bytes = HeifWriter.ToBytes(file);

    var boxType = Encoding.ASCII.GetString(bytes, 4, 4);
    Assert.That(boxType, Is.EqualTo("ftyp"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FtypBrandIsHeic() {
    var file = new HeifFile { Width = 2, Height = 2, PixelData = new byte[12], Brand = "heic" };
    var bytes = HeifWriter.ToBytes(file);

    var brand = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(brand, Is.EqualTo("heic"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMetaBox() {
    var file = new HeifFile { Width = 4, Height = 4, PixelData = new byte[48] };
    var bytes = HeifWriter.ToBytes(file);

    Assert.That(_ContainsBoxType(bytes, "meta"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMdatBox() {
    var file = new HeifFile { Width = 2, Height = 2, PixelData = new byte[12] };
    var bytes = HeifWriter.ToBytes(file);

    Assert.That(_ContainsBoxType(bytes, "mdat"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsIspeWithCorrectDimensions() {
    var file = new HeifFile { Width = 100, Height = 200, PixelData = new byte[60000] };
    var bytes = HeifWriter.ToBytes(file);

    var ispeOffset = _FindBoxTypeOffset(bytes, "ispe");
    Assert.That(ispeOffset, Is.GreaterThan(0));

    // ispe: 8-byte box header + 4-byte fullbox header + width(4) + height(4)
    var widthOffset = ispeOffset + 8 + 4;
    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(widthOffset));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(widthOffset + 4));

    Assert.That(width, Is.EqualTo(100u));
    Assert.That(height, Is.EqualTo(200u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeReasonable() {
    var pixelData = new byte[4 * 4 * 3];
    var file = new HeifFile { Width = 4, Height = 4, PixelData = pixelData };
    var bytes = HeifWriter.ToBytes(file);

    // Must be at least ftyp + meta + mdat headers + pixel data
    Assert.That(bytes.Length, Is.GreaterThan(pixelData.Length));
    // Should not be unreasonably large (max ~2x payload for small images)
    Assert.That(bytes.Length, Is.LessThan(pixelData.Length + 512));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHdlrBox() {
    var file = new HeifFile { Width = 2, Height = 2, PixelData = new byte[12] };
    var bytes = HeifWriter.ToBytes(file);

    Assert.That(_ContainsBoxType(bytes, "hdlr"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPitmBox() {
    var file = new HeifFile { Width = 2, Height = 2, PixelData = new byte[12] };
    var bytes = HeifWriter.ToBytes(file);

    Assert.That(_ContainsBoxType(bytes, "pitm"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UsesRawImageDataWhenPresent() {
    var rawData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var file = new HeifFile { Width = 1, Height = 1, PixelData = new byte[3], RawImageData = rawData };
    var bytes = HeifWriter.ToBytes(file);

    var mdatOffset = _FindBoxTypeOffset(bytes, "mdat");
    Assert.That(mdatOffset, Is.GreaterThan(0));

    // mdat: 8-byte header then data
    var dataStart = mdatOffset + 8;
    var mdatData = new byte[rawData.Length];
    Array.Copy(bytes, dataStart, mdatData, 0, rawData.Length);
    Assert.That(mdatData, Is.EqualTo(rawData));
  }

  // --- Helpers ---

  private static bool _ContainsBoxType(byte[] data, string type) => _FindBoxTypeOffset(data, type) >= 0;

  private static int _FindBoxTypeOffset(byte[] data, string type) {
    var typeBytes = Encoding.ASCII.GetBytes(type);
    for (var i = 4; i <= data.Length - 4; ++i) {
      if (data[i] == typeBytes[0] && data[i + 1] == typeBytes[1] && data[i + 2] == typeBytes[2] && data[i + 3] == typeBytes[3])
        return i - 4; // return box start (size field is 4 bytes before type)
    }

    return -1;
  }
}
