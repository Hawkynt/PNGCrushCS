using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.SunIcon.Tests;

/// <summary>
/// A Sun icon is found by its header and not by the fact that it opens with a comment.
/// </summary>
/// <remarks>
/// The signature declared here was <c>/*</c> and a space, which is not a signature: it is how every
/// C comment starts, and three of the formats in this registry are C source. So an XPM, a PICON or a
/// UIL file offered to the detector was answered "Sun Icon", and the reader behind that answer then
/// threw for want of the <c>Width</c> field it wanted — a file that is readable here, reported as a
/// different format and then refused.
/// <para/>
/// The real header opens <c>/* Format_version=</c>, which is long enough to mean something. XPM
/// declared no signature at all and so could only ever be found by its extension; it declares its
/// own now, and the two no longer collide.
/// </remarks>
[TestFixture]
public sealed class MagicIsNotACommentTests {

  private static byte[] _Xpm() => Encoding.ASCII.GetBytes(
    """
    /* XPM */
    static char * shape_xpm[] = {
    "4 4 2 1",
    "  c #FFFFFF",
    ". c #000000",
    "....",
    ".  .",
    ".  .",
    "...."};
    """);

  private static byte[] _SunIcon() => Encoding.ASCII.GetBytes(
    "/* Format_version=1, Width=16, Height=16, Depth=1, Valid_bits_per_item=16\n */\n"
    + "\t0x0000,0xFFFF,0x8001,0x8001,0x8001,0x8001,0x8001,0x8001,\n"
    + "\t0x8001,0x8001,0x8001,0x8001,0x8001,0x8001,0xFFFF,0x0000,\n");

  [Test]
  [Category("Unit")]
  public void AnXpmIsDetectedAsAnXpm()
    => Assert.That(FormatRegistry.DetectFromBytes(_Xpm()), Is.EqualTo(ImageFormat.Xpm));

  [Test]
  [Category("Unit")]
  public void ASunIconIsStillDetectedAsOne()
    => Assert.That(FormatRegistry.DetectFromBytes(_SunIcon()), Is.EqualTo(ImageFormat.SunIcon));

  /// <summary>And the reader agrees with the detector, both ways round.</summary>
  [Test]
  [Category("Unit")]
  public void TheReaderRefusesAFileThatMerelyOpensWithAComment()
    => Assert.Throws<InvalidDataException>(() => SunIconReader.FromBytes(_Xpm()));

  [Test]
  [Category("Unit")]
  public void TheReaderStillTakesARealOne() {
    var image = SunIconFile.ToRawImage(SunIconReader.FromBytes(_SunIcon()));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(16));
      Assert.That(image.Height, Is.EqualTo(16));
    });
  }
}
