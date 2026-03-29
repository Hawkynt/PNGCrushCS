using System;
using System.Buffers.Binary;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Jpeg2000WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithJp2Signature() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    // JP2 signature: 00 00 00 0C 6A 50 20 20 0D 0A 87 0A
    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x00));
    Assert.That(bytes[2], Is.EqualTo(0x00));
    Assert.That(bytes[3], Is.EqualTo(0x0C));
    Assert.That(bytes[4], Is.EqualTo(0x6A)); // 'j'
    Assert.That(bytes[5], Is.EqualTo(0x50)); // 'P'
    Assert.That(bytes[6], Is.EqualTo(0x20)); // ' '
    Assert.That(bytes[7], Is.EqualTo(0x20)); // ' '
    Assert.That(bytes[8], Is.EqualTo(0x0D));
    Assert.That(bytes[9], Is.EqualTo(0x0A));
    Assert.That(bytes[10], Is.EqualTo(0x87));
    Assert.That(bytes[11], Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFtypBox() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    // ftyp box follows the 12-byte signature
    var ftypType = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
    Assert.That(ftypType, Is.EqualTo(Jp2Box.TYPE_FILE_TYPE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FtypBrandIsJp2() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    // ftyp box data starts at offset 20 (12 sig + 8 ftyp header)
    Assert.That(bytes[20], Is.EqualTo((byte)'j'));
    Assert.That(bytes[21], Is.EqualTo((byte)'p'));
    Assert.That(bytes[22], Is.EqualTo((byte)'2'));
    Assert.That(bytes[23], Is.EqualTo((byte)' '));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsJp2hBox() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    Assert.That(_FindBoxType(bytes, Jp2Box.TYPE_JP2_HEADER), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsJp2cBox() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    Assert.That(_FindBoxType(bytes, Jp2Box.TYPE_CODESTREAM), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CodestreamStartsWithSoc() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    var csOffset = _FindBoxDataOffset(bytes, Jp2Box.TYPE_CODESTREAM);
    Assert.That(csOffset, Is.GreaterThan(0));
    Assert.That(bytes[csOffset], Is.EqualTo(0xFF));
    Assert.That(bytes[csOffset + 1], Is.EqualTo(0x4F));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CodestreamContainsSizMarker() {
    var file = _MakeFile(4, 3, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    var csOffset = _FindBoxDataOffset(bytes, Jp2Box.TYPE_CODESTREAM);
    Assert.That(_FindMarker(bytes, csOffset, bytes.Length, 0xFF51), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CodestreamEndsWithEoc() {
    var file = _MakeFile(2, 2, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    // Find jp2c box, then look for EOC at the end of codestream
    var csOffset = _FindBoxDataOffset(bytes, Jp2Box.TYPE_CODESTREAM);
    var csLength = _FindBoxDataLength(bytes, Jp2Box.TYPE_CODESTREAM);
    var eocOffset = csOffset + csLength - 2;
    Assert.That(bytes[eocOffset], Is.EqualTo(0xFF));
    Assert.That(bytes[eocOffset + 1], Is.EqualTo(0xD9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SizContainsCorrectDimensions() {
    var file = _MakeFile(10, 7, 3);
    var bytes = Jpeg2000Writer.ToBytes(file);

    var csOffset = _FindBoxDataOffset(bytes, Jp2Box.TYPE_CODESTREAM);
    var sizOffset = _FindMarkerOffset(bytes, csOffset, bytes.Length, 0xFF51);
    Assert.That(sizOffset, Is.GreaterThan(0));

    // SIZ: marker(2) + Lsiz(2) + Rsiz(2) + Xsiz(4) + Ysiz(4)
    var xsiz = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizOffset + 6));
    var ysiz = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizOffset + 10));
    Assert.That(xsiz, Is.EqualTo(10u));
    Assert.That(ysiz, Is.EqualTo(7u));
  }

  private static Jpeg2000File _MakeFile(int width, int height, int componentCount) => new() {
    Width = width,
    Height = height,
    ComponentCount = componentCount,
    BitsPerComponent = 8,
    PixelData = new byte[width * height * componentCount],
  };

  private static bool _FindBoxType(byte[] data, uint type) {
    for (var i = 0; i + 8 <= data.Length; ) {
      var boxLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
      var boxType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 4));
      if (boxType == type)
        return true;
      if (boxLen <= 0)
        break;
      i += boxLen;
    }
    return false;
  }

  private static int _FindBoxDataOffset(byte[] data, uint type) {
    for (var i = 0; i + 8 <= data.Length; ) {
      var boxLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
      var boxType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 4));
      if (boxType == type)
        return i + 8;
      if (boxLen <= 0)
        break;
      i += boxLen;
    }
    return -1;
  }

  private static int _FindBoxDataLength(byte[] data, uint type) {
    for (var i = 0; i + 8 <= data.Length; ) {
      var boxLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
      var boxType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 4));
      if (boxType == type)
        return boxLen - 8;
      if (boxLen <= 0)
        break;
      i += boxLen;
    }
    return -1;
  }

  private static bool _FindMarker(byte[] data, int start, int end, ushort marker) {
    for (var i = start; i + 1 < end; ++i)
      if (data[i] == (byte)(marker >> 8) && data[i + 1] == (byte)(marker & 0xFF))
        return true;
    return false;
  }

  private static int _FindMarkerOffset(byte[] data, int start, int end, ushort marker) {
    for (var i = start; i + 1 < end; ++i)
      if (data[i] == (byte)(marker >> 8) && data[i + 1] == (byte)(marker & 0xFF))
        return i;
    return -1;
  }
}
