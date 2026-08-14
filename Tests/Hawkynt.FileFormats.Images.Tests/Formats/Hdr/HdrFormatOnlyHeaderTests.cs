using System;
using System.IO;
using System.Text;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

/// <summary>
/// A Radiance file whose header opens with <c>FORMAT=</c> instead of the <c>#?</c> line.
/// </summary>
/// <remarks>
/// XnView's <c>nconvert -out rad</c> writes <c>FORMAT=32-bit_rle_rgbe</c>, a blank line, the
/// resolution and then RGBE quads, and never writes the <c>#?RADIANCE</c> line the format opens
/// with. nconvert cannot read the result back either — it answers "Don't know how to read this
/// picture" for its own output — so the omission is its bug, not a dialect.
/// <para/>
/// The content is unambiguously Radiance: prepending the missing line makes both nconvert and
/// ImageMagick decode the same file, and the payload is exactly width*height*4 bytes of RGBE.
/// The <c>FORMAT=</c> line naming a Radiance encoding is therefore treated as a signature in its
/// own right rather than the <c>#?</c> test being dropped: a file that opens with neither is still
/// refused.
/// </remarks>
[TestFixture]
public sealed class HdrFormatOnlyHeaderTests {

  /// <summary>Builds the header nconvert writes, with flat (uncompressed) RGBE behind it.</summary>
  private static byte[] _BuildFormatOnlyHdr(int width, int height, byte[] rgbe) {
    var header = Encoding.ASCII.GetBytes($"FORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n");
    var data = new byte[header.Length + rgbe.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(rgbe, 0, data, header.Length, rgbe.Length);
    return data;
  }

  /// <summary>The same file with the line nconvert forgot put back in front.</summary>
  private static byte[] _WithMagic(byte[] formatOnly) {
    var magic = Encoding.ASCII.GetBytes("#?RADIANCE\n");
    var data = new byte[magic.Length + formatOnly.Length];
    Array.Copy(magic, data, magic.Length);
    Array.Copy(formatOnly, 0, data, magic.Length, formatOnly.Length);
    return data;
  }

  private static byte[] _SampleRgbe(int width, int height) {
    var rgbe = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      rgbe[i * 4] = (byte)(i * 3 % 256);
      rgbe[i * 4 + 1] = (byte)(i * 5 % 256);
      rgbe[i * 4 + 2] = 255;
      rgbe[i * 4 + 3] = 128;
    }

    return rgbe;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FormatLineWithoutMagic_IsAccepted() {
    var data = _BuildFormatOnlyHdr(4, 3, _SampleRgbe(4, 3));
    var result = HdrReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(3));
      Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3 * 3));
    });
  }

  /// <summary>
  /// The missing line changes nothing about the picture, so both spellings must decode alike.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithAndWithoutMagic_DecodeIdentically() {
    var formatOnly = _BuildFormatOnlyHdr(9, 5, _SampleRgbe(9, 5));

    var without = HdrReader.FromBytes(formatOnly);
    var with = HdrReader.FromBytes(_WithMagic(formatOnly));

    Assert.Multiple(() => {
      Assert.That(without.Width, Is.EqualTo(with.Width));
      Assert.That(without.Height, Is.EqualTo(with.Height));
      Assert.That(without.PixelData, Is.EqualTo(with.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_XyzeFormatLine_IsAccepted() {
    var header = Encoding.ASCII.GetBytes("FORMAT=32-bit_rle_xyze\n\n-Y 2 +X 2\n");
    var rgbe = _SampleRgbe(2, 2);
    var data = new byte[header.Length + rgbe.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(rgbe, 0, data, header.Length, rgbe.Length);

    Assert.That(HdrReader.FromBytes(data).Width, Is.EqualTo(2));
  }

  /// <summary>A header that names no Radiance encoding is not a Radiance file.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ForeignFormatLine_IsRefused() {
    var data = Encoding.ASCII.GetBytes("FORMAT=something-else\n\n-Y 2 +X 2\n" + new string('\0', 16));
    Assert.Throws<InvalidDataException>(() => HdrReader.FromBytes(data));
  }

  /// <summary>Nothing else gets in: the signature was widened, not dropped.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_NeitherMagicNorFormatLine_IsRefused() {
    var data = Encoding.ASCII.GetBytes("-Y 2 +X 2\n" + new string('\0', 16));
    Assert.Throws<InvalidDataException>(() => HdrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ArbitraryBinary_IsRefused() {
    var data = new byte[64];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7);

    Assert.Throws<InvalidDataException>(() => HdrReader.FromBytes(data));
  }
}
