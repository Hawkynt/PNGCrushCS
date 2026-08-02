using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.SoftImage;

namespace FileFormat.SoftImage.Tests;

[TestFixture]
public sealed class SoftImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic"));
    Assert.Throws<FileNotFoundException>(() => SoftImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[50];
    Assert.Throws<InvalidDataException>(() => SoftImageReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[110];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0xDEADBEEF);
    Assert.Throws<InvalidDataException>(() => SoftImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var data = _BuildMinimalRgb(4, 3);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelData() {
    var data = _BuildMinimalRgb(2, 2);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesComment() {
    var data = _BuildMinimalRgbWithComment(2, 1, "Test Comment");

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Comment, Is.EqualTo("Test Comment"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_HasAlphaIsFalse() {
    var data = _BuildMinimalRgb(2, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_HasAlphaIsTrue() {
    var data = _BuildMinimalRgba(2, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb_Parses() {
    var data = _BuildMinimalRgb(2, 2);

    using var ms = new MemoryStream(data);
    var result = SoftImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesVersion() {
    var data = _BuildMinimalRgb(1, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo(3.71f).Within(0.01f));
  }

  /// <summary>
  /// Writes the header a Softimage PIC really has: the size sits after the four letters
  /// <c>PICT</c>, not where they do, which is what these builders used to assume.
  /// </summary>
  private static void _Header(MemoryStream ms, int width, int height, string comment) {
    var header = new byte[104];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BitConverter.SingleToInt32Bits(3.71f));

    var text = Encoding.ASCII.GetBytes(comment);
    Array.Copy(text, 0, header, 8, Math.Min(text.Length, 80));

    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(88), 0x50494354);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(92), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(94), (ushort)height);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(96), BitConverter.SingleToInt32Bits(1f));

    ms.Write(header, 0, header.Length);
  }

  /// <summary>One scanline as a single stretch of pixels written out, which needs width under 129.</summary>
  private static void _Scanline(MemoryStream ms, int width, Func<int, byte[]> pixel) {
    ms.WriteByte((byte)(width - 1));
    for (var x = 0; x < width; ++x)
      ms.Write(pixel(x));
  }

  private static byte[] _BuildMinimalRgb(int width, int height) {
    using var ms = new MemoryStream();
    _Header(ms, width, height, string.Empty);

    // Red, green and blue in one packet; nothing follows it.
    ms.Write([0, 8, 2, 0x80 | 0x40 | 0x20]);

    for (var y = 0; y < height; ++y)
      _Scanline(ms, width, x => {
        var v = (byte)((y * width + x) % 256);
        return [v, v, v];
      });

    return ms.ToArray();
  }

  private static byte[] _BuildMinimalRgbWithComment(int width, int height, string comment) {
    using var ms = new MemoryStream();
    _Header(ms, width, height, comment);
    ms.Write([0, 8, 2, 0x80 | 0x40 | 0x20]);

    for (var y = 0; y < height; ++y)
      _Scanline(ms, width, _ => [0, 0, 0]);

    return ms.ToArray();
  }

  private static byte[] _BuildMinimalRgba(int width, int height) {
    using var ms = new MemoryStream();
    _Header(ms, width, height, string.Empty);

    // Colour first with more to follow, then alpha on its own — 0x10 is alpha, 0x80 is red.
    ms.Write([1, 8, 2, 0x80 | 0x40 | 0x20]);
    ms.Write([0, 8, 2, 0x10]);

    for (var y = 0; y < height; ++y) {
      _Scanline(ms, width, _ => [0, 0, 0]);
      _Scanline(ms, width, _ => [0xFF]);
    }

    return ms.ToArray();
  }

}
