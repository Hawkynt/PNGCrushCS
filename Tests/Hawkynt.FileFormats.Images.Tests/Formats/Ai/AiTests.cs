using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Illustrator;

namespace FileFormat.Illustrator.Tests;

/// <summary>
/// Fixtures written the way Illustrator writes its headers, one statement at a time.
/// </summary>
/// <remarks>
/// The whole of what this reader adds over the PostScript one is a decision about which files it
/// will not draw, so that is what is tested: a file that declares the procedure sets its drawing is
/// made of and does not carry them is refused by their names, and one that carries them is read.
/// Ghostscript refuses the first kind too, with <c>undefined in Adobe_level2_AI5</c>, which is the
/// same decision reached at a later point.
/// </remarks>
[TestFixture]
public sealed class AiTests {

  /// <summary>The header of an Illustrator file, with whatever resource lines are wanted.</summary>
  private static string _Header(string resources) =>
    "%!PS-Adobe-3.0\n" +
    "%%Creator: Adobe Illustrator(r) 6.0.1\n" +
    "%%BoundingBox: 0 0 100 50\n" +
    "%AI5_FileFormat 2.1\n" +
    resources +
    "%%EndComments\n";

  private static AiFile _Read(string program) => AiReader.FromBytes(Encoding.Latin1.GetBytes(program));

  private static RawImage _Draw(string program) => AiFile.ToRawImage(_Read(program));

  private const string _Drawing = "0 0 100 50 rectfill showpage\n";

  [Test]
  [Category("Unit")]
  public void FileNeedingAProcedureSetItDoesNotCarry_IsRefusedByName() {
    var program = _Header(
      "%%DocumentNeededResources: procset Adobe_level2_AI5 1.0 0\n" +
      "%%+ procset Adobe_Illustrator_AI5 1.0 0\n"
    ) + _Drawing;

    var failure = Assert.Throws<InvalidDataException>(() => _Draw(program));

    Assert.Multiple(() => {
      Assert.That(failure!.Message, Does.Contain("Adobe_level2_AI5"));
      Assert.That(failure.Message, Does.Contain("Adobe_Illustrator_AI5"));
      Assert.That(_Read(program).MissingProcedureSets, Has.Count.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FileCarryingWhatItNeeds_IsDrawn() {
    var program = _Header("%%DocumentSuppliedResources: procset Adobe_Illustrator_AI5 1.0 0\n" +
                          "%%DocumentNeededResources: procset Adobe_Illustrator_AI5 1.0 0\n")
      + "%%BeginProcSet: Adobe_Illustrator_AI5 1.0 0\n/f /fill load def\n%%EndProcSet\n"
      + "0 0 moveto 100 0 lineto 100 50 lineto 0 50 lineto closepath f showpage\n";

    var image = _Draw(program);

    Assert.Multiple(() => {
      Assert.That(_Read(program).MissingProcedureSets, Is.Empty);
      Assert.That(image.Width, Is.EqualTo(133));
      Assert.That(image.Height, Is.EqualTo(67));
      Assert.That(image.PixelData[0], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ContinuationLines_CountTowardsTheSameList() {
    // A resource list runs on with %%+, and a set named there is as much a requirement as the first.
    var program = _Header(
      "%%DocumentNeededResources: procset Adobe_level2_AI5 1.0 0\n" +
      "%%+ procset Adobe_screens_AI5 1.0 0\n" +
      "%%+ procset Adobe_blend_AI5 1.0 0\n"
    ) + _Drawing;

    Assert.That(_Read(program).MissingProcedureSets, Has.Count.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FontsNamedAndNotCarried_AreNotAReasonToRefuse() {
    // Only procedure sets are counted. A font this reader could not draw with anyway does not stop
    // the geometry from being drawn.
    var program = _Header("%%DocumentNeededResources: font Helvetica\n") + _Drawing;

    Assert.Multiple(() => {
      Assert.That(_Read(program).MissingProcedureSets, Is.Empty);
      Assert.DoesNotThrow(() => _Draw(program));
    });
  }

  [Test]
  [Category("Unit")]
  public void VersionNineAndLater_IsAPdfAndSaysSo() {
    var failure = Assert.Throws<InvalidDataException>(() => _Read("%PDF-1.5\n%\xE2\xE3\xCF\xD3\n1 0 obj\n"));

    Assert.Multiple(() => {
      Assert.That(failure!.Message, Does.Contain("PDF"));

      // Saying no rather than nothing is what sends such a file to the reader that can open it.
      Assert.That(_Matches<AiFile>("%PDF-1.5"u8), Is.False);
      Assert.That(_Matches<AiFile>("%!PS-Adobe-3.0"u8), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void CreatorVersion_IsReadOutOfTheFilesOwnComment() {
    var program = _Header("") + _Drawing;
    Assert.That(_Read(program).Version, Does.Contain("2.1"));
  }

  [Test]
  [Category("Unit")]
  public void ClaimedName_IsTheOneIllustratorUses() {
    Assert.That(_Extensions<AiFile>(), Is.EqualTo(new[] { ".ai" }));
  }

  [Test]
  [Category("Unit")]
  public void SomethingThatIsNeitherPostScriptNorPdf_IsRefused() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => _Read("this is not a drawing"));
      Assert.Throws<InvalidDataException>(() => AiReader.FromBytes([0x89, 0x50, 0x4E, 0x47]));
    });
  }

  private static string[] _Extensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  private static bool? _Matches<T>(ReadOnlySpan<byte> header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);
}
