using System.Text;
using FileFormat.Core;
using FileFormat.PostScript;

namespace FileFormat.PostScript.Tests;

/// <summary>
/// Holds a written PostScript program to stating how big its page is, twice, to the two readers that
/// each read one of the statements.
/// </summary>
/// <remarks>
/// A program that states its size once states it to nobody. <c>%%BoundingBox</c> is a comment and no
/// interpreter acts on it — it is what a DSC-aware cropper reads, and DSC says it is four integers,
/// so a file that offers only <c>%%HiResBoundingBox</c> offers such a tool nothing it can use.
/// <c>setpagedevice</c> is the other half, the one the interpreter acts on, and a file without it
/// gets whatever medium the interpreter defaults to. This writer had neither: Ghostscript rendered a
/// 320 by 200 picture onto 595 by 842 points of A4 with the picture in one corner.
/// <para/>
/// The size itself is one point to the sample. A raster states no physical size of its own, so
/// writing it into a page description language means picking one, and this is the pick the EPS and
/// PDF writers here already made and the one an interpreter's default resolution agrees with.
/// </remarks>
[TestFixture]
public sealed class PostScriptPageTests {

  private const int _WIDTH = 23;
  private const int _HEIGHT = 17;

  private static string _Written() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 251);

    var image = new RawImage {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    return Encoding.ASCII.GetString(PostScriptWriter.ToBytes(PostScriptFile.FromRawImage(image)));
  }

  [Test]
  [Category("Unit")]
  public void TheProgramStatesItsBoxAsWholeNumbersForACropper() {
    Assert.That(_Written(), Does.Contain($"%%BoundingBox: 0 0 {_WIDTH} {_HEIGHT}"),
      "DSC states the box as four integers, and a tool handed a fraction there either refuses the "
      + "line or truncates it.");
  }

  [Test]
  [Category("Unit")]
  public void TheProgramAsksForAMediumTheSizeOfItsPicture() {
    Assert.That(_Written(), Does.Contain($"<< /PageSize [{_WIDTH} {_HEIGHT}] >> setpagedevice"),
      "Without this the interpreter keeps its default medium and the picture lands in the corner of "
      + "a sheet of A4.");
  }

  [Test]
  [Category("Unit")]
  public void TheBoxTheCommentStatesIsTheBoxTheDrawingFills() {
    var program = _Written();

    Assert.Multiple(() => {
      Assert.That(program, Does.Contain($"{_WIDTH} {_HEIGHT} scale"),
        "The unit square the image operator draws on has to be stretched over exactly the box the "
        + "file says it occupies, or the two statements describe different pictures.");
      Assert.That(program, Does.Contain($"%%HiResBoundingBox: 0 0 {_WIDTH} {_HEIGHT}"));
    });
  }
}
