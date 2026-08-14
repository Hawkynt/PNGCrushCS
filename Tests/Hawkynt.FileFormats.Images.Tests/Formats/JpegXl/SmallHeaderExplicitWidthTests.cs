using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The small size header can spell its width out instead of deriving it from a ratio.
/// </summary>
/// <remarks>
/// The small header is a height in eighths and then three bits of aspect ratio, and the ratio
/// codes cover seven shapes. Nothing said what happens when the picture is none of them, and the
/// answer is ratio zero followed by the width, in eighths, exactly as the height was written. The
/// reader here skipped that field: it took the ratio, found zero, fell through the seven cases and
/// left the width equal to the height. A 40 by 24 image measured 24 by 24, and the bit reader was
/// left five bits short of where the next header begins, so everything after it was read crooked.
/// <para/>
/// The bytes below are not constructed from the specification, they are what libjxl 0.12.0 wrote:
/// <c>cjxl</c> on a 40 by 24 picture produces a codestream opening <c>FF 0A 05 08</c>, and those
/// two bytes after the signature are this header. Read bit by bit, least significant first, they
/// are: small, height_div8 = 2, ratio = 0, width_div8 = 4 — twenty-four high and forty wide.
/// </remarks>
[TestFixture]
public sealed class SmallHeaderExplicitWidthTests {

  /// <summary>The two header bytes libjxl writes for a 40 by 24 picture.</summary>
  private static readonly byte[] _LibJxl40By24 = [0x05, 0x08];

  [Test]
  [Category("Unit")]
  public void TheHeaderLibJxlWritesFor40x24IsRead40x24() {
    var (width, height, _) = JpegXlSizeHeader.Decode(_LibJxl40By24);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(40));
      Assert.That(height, Is.EqualTo(24));
    });
  }

  /// <summary>
  /// And the field is consumed, not merely skipped over.
  /// </summary>
  /// <remarks>
  /// Fourteen bits of header round up to two bytes. A reader that stopped after the ratio would
  /// claim one, and every field after the size would then be read from the wrong bit.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void TheWidthFieldIsConsumed()
    => Assert.That(JpegXlSizeHeader.Decode(_LibJxl40By24).BytesConsumed, Is.EqualTo(2));

  /// <summary>What we write for that size is what libjxl writes for it.</summary>
  [Test]
  [Category("Unit")]
  public void WeWriteTheSameHeaderLibJxlDoes()
    => Assert.That(JpegXlSizeHeader.Encode(40, 24), Is.EqualTo(_LibJxl40By24));

  /// <summary>
  /// The seven ratios still take the shapes they always did.
  /// </summary>
  /// <remarks>
  /// A square is ratio 1 and stays two bytes; spelling its width out as well would be legal and
  /// longer, and libjxl does not do it.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ARatioThatFitsIsStillUsed()
    => Assert.That(JpegXlSizeHeader.Encode(8, 8), Is.EqualTo(new byte[] { 0x41, 0x00 }));

  [Test]
  [Category("Unit")]
  public void TheSizeSurvivesItsOwnRoundTrip(
    [Values(40, 48, 56, 64, 72, 80)] int width,
    [Values(24, 32, 40)] int height) {
    var (backWidth, backHeight, _) = JpegXlSizeHeader.Decode(JpegXlSizeHeader.Encode(width, height));

    Assert.Multiple(() => {
      Assert.That(backWidth, Is.EqualTo(width));
      Assert.That(backHeight, Is.EqualTo(height));
    });
  }
}
