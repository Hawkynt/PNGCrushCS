using System;
using System.Text;
using FileFormat.Core;
using FileFormat.Illustrator;

namespace FileFormat.Illustrator.Tests;

/// <summary>
/// Holds a written Illustrator document to defining the private operator it goes on to use.
/// </summary>
/// <remarks>
/// <c>XI</c> is Adobe's raster operator and there is no such thing in the PostScript language, so a
/// document that uses it and defines it nowhere runs nowhere but Illustrator. Ghostscript stopped on
/// it outright — <c>Error: /undefined in XI</c>, with the whole operand list still on the stack —
/// and drew nothing.
/// <para/>
/// The fix is not to stop using the operator, which is what makes the file an Illustrator document
/// rather than a PostScript file with an Illustrator extension: the raster stays an object that can
/// be selected and edited instead of marks on a page. It is to carry the definition, which is what
/// Illustrator's own files do and have always done — they open with the
/// <c>Adobe_Illustrator_AI5</c> procedure set, defining every private operator the document uses.
/// </remarks>
[TestFixture]
public sealed class AiProgramTests {

  private const int _WIDTH = 13;
  private const int _HEIGHT = 9;

  private static string _Written() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 251);

    var image = new RawImage {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    return Encoding.ASCII.GetString(AiWriter.ToBytes(AiFile.FromRawImage(image)));
  }

  [Test]
  [Category("Unit")]
  public void TheDocumentDefinesTheRasterOperatorBeforeItUsesIt() {
    var document = _Written();
    var defined = document.IndexOf("/XI {", StringComparison.Ordinal);
    var used = document.IndexOf(" XI\n", StringComparison.Ordinal);

    Assert.Multiple(() => {
      Assert.That(defined, Is.GreaterThanOrEqualTo(0),
        "An interpreter that is not Illustrator has never heard of XI, so a document using it has to "
        + "say what it means.");
      Assert.That(used, Is.GreaterThanOrEqualTo(0), "The raster is still written as a native XI object.");
      Assert.That(defined, Is.LessThan(used), "A definition after the use is a definition nothing reaches.");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDefinitionIsCarriedWhereADocumentCarriesItsProcedureSets() {
    var document = _Written();
    var prologue = document.IndexOf("%%BeginProlog", StringComparison.Ordinal);
    var epilogue = document.IndexOf("%%EndProlog", StringComparison.Ordinal);
    var defined = document.IndexOf("/XI {", StringComparison.Ordinal);

    Assert.Multiple(() => {
      Assert.That(document, Does.Contain($"%%BeginResource: procset {AiRasterProcSet.Name}"),
        "A procedure set the document supplies is announced as one, so a reader of the comments knows "
        + "the document is self-contained.");
      Assert.That(defined, Is.InRange(prologue, epilogue));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheArtboardIsAPointToTheSample() {
    var document = _Written();

    Assert.Multiple(() => {
      Assert.That(document, Does.Contain($"%%BoundingBox: 0 0 {_WIDTH} {_HEIGHT}"));
      Assert.That(document, Does.Contain($"<< /PageSize [{_WIDTH} {_HEIGHT}] >> setpagedevice"),
        "An .ai document is a page of its own, not artwork pasted onto somebody else's, so it says "
        + "how big that page is rather than taking the interpreter's default medium.");
      Assert.That(document, Does.Contain("[ 1 0 0 1 0 0 ] "),
        "With a point to the sample the raster's own matrix is the identity.");
    });
  }
}
