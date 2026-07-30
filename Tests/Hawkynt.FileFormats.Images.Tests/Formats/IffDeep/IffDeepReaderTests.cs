using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.IffDeep;

namespace FileFormat.IffDeep.Tests;

[TestFixture]
public sealed class IffDeepReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDeepReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDeepReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".deep"));
    Assert.Throws<FileNotFoundException>(() => IffDeepReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDeepReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffDeepReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormMagic_ThrowsInvalidDataException() {
    var data = new byte[24];
    Encoding.ASCII.GetBytes("XXXX", data.AsSpan(0));
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 16);
    Encoding.ASCII.GetBytes("DEEP", data.AsSpan(8));
    Assert.Throws<InvalidDataException>(() => IffDeepReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var data = new byte[24];
    Encoding.ASCII.GetBytes("FORM", data.AsSpan(0));
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 16);
    Encoding.ASCII.GetBytes("ILBM", data.AsSpan(8));
    Assert.Throws<InvalidDataException>(() => IffDeepReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingDgbl_ThrowsInvalidDataException() {
    // FORM + DEEP with no DGBL chunk
    var data = _BuildMinimalDeep(2, 2, false, IffDeepCompression.None, skipDgbl: true);
    Assert.Throws<InvalidDataException>(() => IffDeepReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var data = _BuildMinimalDeep(4, 3, false, IffDeepCompression.None);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_HasAlphaIsFalse() {
    var data = _BuildMinimalDeep(2, 2, false, IffDeepCompression.None);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_PixelDataLength() {
    var data = _BuildMinimalDeep(4, 3, false, IffDeepCompression.None);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_HasAlphaIsTrue() {
    var data = _BuildMinimalDeep(2, 2, true, IffDeepCompression.None);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_PixelDataLength() {
    var data = _BuildMinimalDeep(2, 2, true, IffDeepCompression.None);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_PixelDataPreserved() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);

    var data = _BuildMinimalDeep(2, 2, false, IffDeepCompression.None, pixels);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalDeep(4, 3, false, IffDeepCompression.None);
    using var ms = new MemoryStream(data);
    var result = IffDeepReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RleCompressed_DecompressesCorrectly() {
    var pixels = new byte[4 * 3 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var compressed = ByteRun1Compressor.Encode(pixels);
    var data = _BuildMinimalDeep(4, 3, false, IffDeepCompression.Rle, compressed);
    var result = IffDeepReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
    Assert.That(result.Compression, Is.EqualTo(IffDeepCompression.Rle));
  }

  /// <summary>Builds a minimal valid IFF DEEP file.</summary>
  private static byte[] _BuildMinimalDeep(
    int width,
    int height,
    bool hasAlpha,
    IffDeepCompression compression,
    byte[]? bodyPixels = null,
    bool skipDgbl = false
  ) {
    using var ms = new MemoryStream();

    // FORM header placeholder
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    ms.Write(new byte[4]); // size placeholder

    // Form type
    ms.Write(Encoding.ASCII.GetBytes("DEEP"));

    if (!skipDgbl) {
      // DGBL chunk
      var elemCount = hasAlpha ? 4 : 3;
      ms.Write(Encoding.ASCII.GetBytes("DGBL"));
      _WriteInt32BE(ms, 8);
      _WriteUInt16BE(ms, (ushort)width);
      _WriteUInt16BE(ms, (ushort)height);
      _WriteUInt16BE(ms, (ushort)compression);
      _WriteUInt16BE(ms, (ushort)elemCount);

      // DPEL chunk
      var dpelData = new byte[elemCount * 4];
      var dpelSpan = dpelData.AsSpan();
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan, 0);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[2..], 8);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[4..], 0);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[6..], 8);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[8..], 0);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[10..], 8);
      if (hasAlpha) {
        BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[12..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[14..], 8);
      }

      ms.Write(Encoding.ASCII.GetBytes("DPEL"));
      _WriteInt32BE(ms, dpelData.Length);
      ms.Write(dpelData);
      if ((dpelData.Length & 1) != 0)
        ms.WriteByte(0);

      // BODY chunk
      var bpp = hasAlpha ? 4 : 3;
      var body = bodyPixels ?? new byte[width * height * bpp];
      ms.Write(Encoding.ASCII.GetBytes("BODY"));
      _WriteInt32BE(ms, body.Length);
      ms.Write(body);
      if ((body.Length & 1) != 0)
        ms.WriteByte(0);
    }

    // Patch FORM size
    var result = ms.ToArray();
    var formSize = result.Length - 8;
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), formSize);

    return result;
  }

  private static void _WriteInt32BE(Stream s, int v) {
    Span<byte> b = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(b, v);
    s.Write(b);
  }

  private static void _WriteUInt16BE(Stream s, ushort v) {
    Span<byte> b = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(b, v);
    s.Write(b);
  }
}
