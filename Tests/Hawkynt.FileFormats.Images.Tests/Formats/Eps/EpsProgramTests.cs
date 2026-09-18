using System;
using System.Text;
using FileFormat.Eps;

namespace FileFormat.Eps.Tests;

/// <summary>
/// Holds the PostScript half of a DOS EPS to the thing an EPS is for.
/// </summary>
/// <remarks>
/// An EPS carries the picture twice: once as a preview a layout application can show without an
/// interpreter, and once as the program that actually gets printed. Only the second one is the file
/// — the preview is a convenience and every specification says so. This writer used to produce a
/// program of eighty bytes that stated a bounding box and called <c>showpage</c>, so the printed
/// result was an empty rectangle while every tool that appeared to read the file was reading the
/// TIFF. Ghostscript, given a page painted black first so that "drew nothing" can be told from
/// "drew white", returned a picture whose mean was exactly zero.
/// <para/>
/// So the preview is not what is measured here. The program is found the way a consumer finds it,
/// through the offsets in the binary header, and is asked whether it paints as many samples as the
/// picture has.
/// </remarks>
[TestFixture]
public sealed class EpsProgramTests {

  private const int _WIDTH = 7;
  private const int _HEIGHT = 5;

  private static byte[] _Written() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 251);

    return EpsWriter.ToBytes(new EpsFile { Width = _WIDTH, Height = _HEIGHT, PixelData = pixels });
  }

  /// <summary>The PostScript section, found the way the binary header says to find it.</summary>
  private static string _Program(byte[] file) {
    var header = EpsHeader.ReadFrom(file.AsSpan(0, EpsHeader.StructSize));

    return Encoding.ASCII.GetString(file, (int)header.PsOffset, (int)header.PsLength);
  }

  [Test]
  [Category("Unit")]
  public void ThePostScriptSectionPaintsEverySampleThePictureHas() {
    var program = _Program(_Written());

    Assert.That(program, Does.Contain("colorimage"),
      "The PostScript section is the file. A section that states a box and calls showpage prints an "
      + "empty rectangle, however good the preview beside it looks.");

    // Between the operator and the restore that closes it is the sample data and nothing else.
    var from = program.IndexOf("colorimage", StringComparison.Ordinal) + "colorimage".Length;
    var to = program.IndexOf("grestore", from, StringComparison.Ordinal);
    Assert.That(to, Is.GreaterThan(from), "The drawing is never closed.");

    var digits = 0;
    foreach (var character in program.AsSpan(from, to - from))
      if (character is (>= '0' and <= '9') or (>= 'A' and <= 'F'))
        ++digits;

    Assert.That(digits, Is.EqualTo(_WIDTH * _HEIGHT * 3 * 2),
      "Two hexadecimal digits to the sample, and three samples to the pixel, is what the image "
      + "operator is going to read out of the file after it.");
  }

  [Test]
  [Category("Unit")]
  public void ThePostScriptSectionStatesTheBoxAtAPointToTheSample() {
    var program = _Program(_Written());

    Assert.Multiple(() => {
      Assert.That(program, Does.StartWith("%!PS-Adobe-3.0 EPSF-3.0"));
      Assert.That(program, Does.Contain($"%%BoundingBox: 0 0 {_WIDTH} {_HEIGHT}"));

      // An EPS is artwork pasted onto somebody else's page, and the specification forbids it from
      // changing the medium under them. The bounding box is the only size it gets to state.
      Assert.That(program, Does.Not.Contain("setpagedevice"),
        "An encapsulated program may not set the page device of the job that includes it.");
    });
  }
}
