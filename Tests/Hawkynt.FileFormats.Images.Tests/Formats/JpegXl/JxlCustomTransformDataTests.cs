using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The bundle between the image metadata and the first frame, and what happens
/// to the frame when it is skipped.
/// </summary>
/// <remarks>
/// The bundle is one bit long whenever a file leaves it alone, so omitting it
/// is invisible in almost every file: the byte alignment that follows the
/// metadata swallows the bit that was never read. It only shows itself when
/// the metadata ends on a byte boundary and the alignment has nothing to
/// swallow, and then the frame header, the table of contents and every section
/// offset after it are one bit out.
///
/// <para>The header bytes below are the opening of two files cjxl 0.12.0
/// wrote. They are checked against something that cannot be fudged: the single
/// section each table of contents states, plus the bytes of header in front of
/// it, has to come to the length of the file it came from.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlCustomTransformDataTests {

  /// <summary>First twelve bytes of a 200x150 lossy file of 9470 bytes. Its
  /// metadata ends on a byte boundary, which is what made the missing bundle
  /// visible.</summary>
  private static readonly byte[] _MetadataEndingOnAByteBoundary =
    [0xFF, 0x0A, 0xA8, 0xB4, 0x01, 0x00, 0x13, 0x88, 0x02, 0x00, 0xC9, 0x83];

  private const int _BoundaryFileLength = 9470;

  /// <summary>First eleven bytes of a 64x64 lossy file of 81 bytes. Its
  /// metadata ends mid-byte, so this one parsed even while the bundle went
  /// unread.</summary>
  private static readonly byte[] _MetadataEndingMidByte =
    [0xFF, 0x0A, 0x4F, 0x06, 0x00, 0x13, 0x88, 0x02, 0x00, 0x18, 0x01];

  private const int _MidByteFileLength = 81;

  private static (int Width, int Height, JxlSpecFrameHeader Frame, JxlFrameToc Toc, int Body) _ReadHeaders(byte[] data) {
    var reader = new JxlBitReader(data, 2);
    var (width, height) = JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    reader.ZeroPadToByte();
    var frame = JxlSpecFrameHeader.Decode(reader, metadata);
    var toc = JxlFrameToc.Decode(reader, numGroups: 1, (int)frame.NumPasses, numDcGroups: 1);
    return (width, height, frame, toc, (int)(reader.BitsRead / 8));
  }

  [Test]
  public void AFileWhoseMetadataEndsOnAByteBoundaryStillFindsItsFrame() {
    var (width, height, frame, toc, body) = _ReadHeaders(_MetadataEndingOnAByteBoundary);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(200));
      Assert.That(height, Is.EqualTo(150));
      Assert.That(frame.Encoding, Is.EqualTo(JxlFrameEncoding.VarDct));
      Assert.That(toc.SectionSizes, Has.Length.EqualTo(1));
      Assert.That(body + toc.SectionSizes[0], Is.EqualTo(_BoundaryFileLength), "header plus section is the file");
    });
  }

  [Test]
  public void AFileWhoseMetadataEndsMidByteIsUnaffected() {
    var (width, height, frame, toc, body) = _ReadHeaders(_MetadataEndingMidByte);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(64));
      Assert.That(height, Is.EqualTo(64));
      Assert.That(frame.Encoding, Is.EqualTo(JxlFrameEncoding.VarDct));
      Assert.That(toc.SectionSizes, Has.Length.EqualTo(1));
      Assert.That(body + toc.SectionSizes[0], Is.EqualTo(_MidByteFileLength), "header plus section is the file");
    });
  }

  /// <summary>A file that leaves the bundle alone spends exactly one bit on it.</summary>
  [Test]
  public void TheDefaultBundleIsOneBit() {
    var reader = new JxlBitReader([0xFF], 0);
    var data = JxlCustomTransformData.Decode(reader, xybEncoded: true);

    Assert.Multiple(() => {
      Assert.That(data.AllDefault, Is.True);
      Assert.That(reader.BitsRead, Is.EqualTo(1));
    });
  }

  /// <summary>A file that states its own inverse opsin matrix spends sixteen
  /// half-precision values on it: nine coefficients, three opsin biases and
  /// four quantization biases.</summary>
  [Test]
  public void AStatedOpsinMatrixIsSixteenHalfPrecisionValues() {
    var bytes = new byte[64];
    var reader = new JxlBitReader(bytes, 0);
    var data = JxlCustomTransformData.Decode(reader, xybEncoded: true);

    Assert.Multiple(() => {
      Assert.That(data.AllDefault, Is.False);
      Assert.That(data.OpsinAllDefault, Is.False);
      Assert.That(data.OpsinValues, Has.Length.EqualTo(16));
      Assert.That(data.CustomWeightsMask, Is.EqualTo(0u));
      // all_default, opsin all_default, 16 x F16, then the 3-bit weights mask.
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 1 + 16 * 16 + 3));
    });
  }

  /// <summary>When the file is not XYB encoded there is no opsin matrix to
  /// state, so the bundle goes straight to the upsampling weights.</summary>
  [Test]
  public void WithoutXybThereIsNoOpsinMatrixInTheBundle() {
    var reader = new JxlBitReader([0x00], 0);
    var data = JxlCustomTransformData.Decode(reader, xybEncoded: false);

    Assert.Multiple(() => {
      Assert.That(data.AllDefault, Is.False);
      Assert.That(data.OpsinValues, Is.Empty);
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 3));
    });
  }
}
