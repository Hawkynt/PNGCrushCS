using System;
using System.Text;
using FileFormat.Cals;
using FileFormat.Core;

namespace FileFormat.Cals.Tests;

/// <summary>Reads a CALS file laid out the way the rest of the world writes one.</summary>
/// <remarks>
/// A type 1 raster is Group 4 compressed — that is what the type means — but the payload was being
/// copied out as if it were already pixels. A 40x24 image needs 120 bytes of those and this file
/// carries 12, so the reader took the compressed bytes as the top of the picture and left the rest
/// zero: every CALS file came out solid black. Nothing caught it because the writer skipped the
/// compression too, so the two agreed.
/// </remarks>
[TestFixture]
public sealed class CalsForeignFileTests {

  private const int _WIDTH = 40;
  private const int _HEIGHT = 24;

  /// <summary>The Group 4 payload ImageMagick wrote for a 40x24 image split down the middle.</summary>
  private static readonly byte[] _HalfAndHalfPayload =
    [0x22, 0x03, 0x47, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xE0, 0x02, 0x00, 0x20];

  [Test]
  [Category("Unit")]
  public void Read_ForeignFile_ExpandsTheCompressedPayload() {
    var file = CalsReader.FromBytes(_Build(_HalfAndHalfPayload));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.PixelData, Has.Length.EqualTo(5 * _HEIGHT), "the 12 compressed bytes expand to a full raster");
    });
  }

  /// <summary>
  /// CALS runs the opposite way round from the coding it is compressed with: a Group 4 white run is
  /// black ink here. Checked against ImageMagick, which renders the left half of this file black.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Read_ForeignFile_PutsTheDarkHalfWhereOtherReadersDo() {
    var rgb = CalsFile.ToRawImage(CalsReader.FromBytes(_Build(_HalfAndHalfPayload))).ToRgb24();

    byte Red(int x, int y) => rgb[((y * _WIDTH) + x) * 3];

    Assert.Multiple(() => {
      Assert.That(Red(10, 6), Is.EqualTo(0), "top left is black");
      Assert.That(Red(30, 6), Is.EqualTo(255), "top right is white");
      Assert.That(Red(10, 18), Is.EqualTo(0), "bottom left is black");
      Assert.That(Red(30, 18), Is.EqualTo(255), "bottom right is white");
    });
  }

  [Test]
  [Category("Unit")]
  public void Write_ThenRead_CompressesAndComesBackTheSame() {
    var original = CalsReader.FromBytes(_Build(_HalfAndHalfPayload));
    var written = CalsWriter.ToBytes(original);

    Assert.That(
      written.Length,
      Is.LessThan(CalsHeaderParser.HeaderSize + (5 * _HEIGHT)),
      "the payload is compressed, not written through");

    Assert.That(CalsReader.FromBytes(written).PixelData, Is.EqualTo(original.PixelData));
  }

  /// <summary>Builds the 2048-byte header ImageMagick writes, then the payload.</summary>
  private static byte[] _Build(byte[] payload) {
    string[] records = [
      "srcdocid: NONE",
      "dstdocid: NONE",
      "txtfilid: NONE",
      "figid: NONE",
      "srcgph: NONE",
      "doccls: NONE",
      "rtype: 1",
      "rorient: 000,270",
      $"rpelcnt: {_WIDTH:D6},{_HEIGHT:D6}",
      "rdensty: 0200",
      "notes: NONE",
    ];

    var data = new byte[CalsHeaderParser.HeaderSize + payload.Length];
    for (var i = 0; i < records.Length; ++i)
      Encoding.ASCII.GetBytes(records[i]).CopyTo(data, i * 128); // one 128-byte record each

    payload.CopyTo(data, CalsHeaderParser.HeaderSize);
    return data;
  }
}
