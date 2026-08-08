using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Core.Vector;
using FileFormat.PostScript;

namespace FileFormat.PostScript.Tests;

/// <summary>
/// Programs written here rather than sampled, each one exercising a part of the language on its own.
/// </summary>
/// <remarks>
/// A PostScript reader is an interpreter, so it is tested as one: the stack machine is driven
/// directly with programs whose answer is arithmetic, and the graphics are driven with programs
/// whose answer is a shape whose area on the page can be worked out beforehand. A fixture that draws
/// a triangle of a stated size covers a known share of a page of a stated size, and that share is
/// what is checked — not a hash of the pixels, which would say nothing about what was wrong when it
/// changed.
/// </remarks>
[TestFixture]
public sealed class PostScriptTests {

  /// <summary>A hundred point square page, which at ninety-six to the inch is 133 pixels each way.</summary>
  private const string _Header = "%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 100 100\n%%EndComments\n";

  private static PostScriptFile _Read(string program) => PostScriptReader.FromBytes(Encoding.Latin1.GetBytes(program));

  private static RawImage _Draw(string body) => PostScriptFile.ToRawImage(_Read(_Header + body));

  private static PostScriptRendering _Render(string body) => PostScriptRenderer.Render(_Read(_Header + body));

  /// <summary>How much of the picture is ink, taking the page as white paper.</summary>
  private static double _Coverage(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }

  /// <summary>The colour at a point, in pixels from the top left.</summary>
  private static (int R, int G, int B) _At(RawImage image, int x, int y) {
    var at = (y * image.Width + x) * 4;
    return (image.PixelData[at], image.PixelData[at + 1], image.PixelData[at + 2]);
  }

  #region the stack machine on its own

  /// <summary>
  /// Runs a program that leaves numbers on the stack and reads them back.
  /// </summary>
  /// <remarks>
  /// The interpreter draws onto a page whether or not the program does, so the smallest page that
  /// can exist is used and nothing is drawn on it. What is being measured is the operand stack.
  /// </remarks>
  private static double[] _Stack(string program, int expected) {
    var canvas = new VectorCanvas(1, 1, Rgba32.White);
    var page = new PsPage(canvas, Matrix2D.Identity);
    var bytes = Encoding.Latin1.GetBytes(program);
    var interpreter = new PostScriptInterpreter(bytes, 0, bytes.Length, page);
    interpreter.Run();

    Assert.That(interpreter.Count, Is.EqualTo(expected), $"\"{program}\" left {interpreter.Count} operands");

    var values = new double[expected];
    for (var i = expected - 1; i >= 0; --i) {
      var value = interpreter.Pop();
      Assert.That(value.IsNumber, Is.True, $"\"{program}\" left {value.TypeName} where a number belongs");
      values[i] = value.Number;
    }

    return values;
  }

  [Test]
  [Category("Unit")]
  public void Arithmetic_FollowsTheOperandOrderTheLanguageStates() {
    Assert.Multiple(() => {
      Assert.That(_Stack("7 3 sub", 1)[0], Is.EqualTo(4));
      Assert.That(_Stack("7 3 div", 1)[0], Is.EqualTo(7.0 / 3).Within(1e-12));
      Assert.That(_Stack("7 3 idiv", 1)[0], Is.EqualTo(2));
      Assert.That(_Stack("7 3 mod", 1)[0], Is.EqualTo(1));
      Assert.That(_Stack("2 10 exp", 1)[0], Is.EqualTo(1024).Within(1e-9));
      Assert.That(_Stack("0 1 atan", 1)[0], Is.EqualTo(0).Within(1e-9));
      Assert.That(_Stack("1 0 atan", 1)[0], Is.EqualTo(90).Within(1e-9));
    });
  }

  [Test]
  [Category("Unit")]
  public void IntegerArithmetic_StaysIntegerSoAnIndexCanComeOutOfIt() {
    var canvas = new VectorCanvas(1, 1, Rgba32.White);
    var page = new PsPage(canvas, Matrix2D.Identity);
    var bytes = Encoding.Latin1.GetBytes("[10 20 30] 1 2 add 1 sub get");
    var interpreter = new PostScriptInterpreter(bytes, 0, bytes.Length, page);
    interpreter.Run();

    Assert.That(interpreter.Pop().Number, Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void Roll_MovesTheTopmostOperandsRoundBothWays() {
    Assert.Multiple(() => {
      Assert.That(_Stack("1 2 3 3 1 roll", 3), Is.EqualTo(new double[] { 3, 1, 2 }));
      Assert.That(_Stack("1 2 3 3 -1 roll", 3), Is.EqualTo(new double[] { 2, 3, 1 }));
      Assert.That(_Stack("1 2 3 3 0 roll", 3), Is.EqualTo(new double[] { 1, 2, 3 }));
      Assert.That(_Stack("1 2 3 2 index", 4), Is.EqualTo(new double[] { 1, 2, 3, 1 }));
      Assert.That(_Stack("1 2 3 2 copy", 5), Is.EqualTo(new double[] { 1, 2, 3, 2, 3 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Procedure_IsPushedWhereItIsWrittenAndRunWhereItIsNamed() {
    // The distinction the whole language rests on: a procedure met in the program goes on the stack,
    // and only exec, a control operator, or a name standing for it runs it.
    Assert.Multiple(() => {
      Assert.That(_Stack("/twice {2 mul} def 21 twice", 1)[0], Is.EqualTo(42));
      Assert.That(_Stack("{1 2 add} exec", 1)[0], Is.EqualTo(3));
      Assert.That(_Stack("{1 2 add} length", 1)[0], Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Loops_CountAndCanBeLeftEarly() {
    Assert.Multiple(() => {
      Assert.That(_Stack("0 1 1 10 {add} for", 1)[0], Is.EqualTo(55));
      Assert.That(_Stack("0 10 {1 add} repeat", 1)[0], Is.EqualTo(10));
      Assert.That(_Stack("0 {1 add dup 5 ge {exit} if} loop", 1)[0], Is.EqualTo(5));
      Assert.That(_Stack("0 [1 2 3 4] {add} forall", 1)[0], Is.EqualTo(10));
      Assert.That(_Stack("0 (abc) {add} forall", 1)[0], Is.EqualTo(97 + 98 + 99));
    });
  }

  [Test]
  [Category("Unit")]
  public void ForAllOverADictionary_HandsTheBodyAKeyAndItsValue() {
    // Two objects a turn rather than one, which is the only place the loop machinery differs.
    Assert.That(_Stack("0 << /a 1 /b 2 >> {exch pop add} forall", 1)[0], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Stopped_CatchesWhatTheLanguageDefinesAsAnError() {
    // A program asking whether an operator exists is not a program going wrong, and this is how it
    // asks. Answering it the way the language says is what lets a file take its own other route.
    Assert.Multiple(() => {
      Assert.That(_Stack("{undefinedoperator} stopped {1} {2} ifelse", 1)[0], Is.EqualTo(1));
      Assert.That(_Stack("{1 1 add pop} stopped {1} {2} ifelse", 1)[0], Is.EqualTo(2));
      Assert.That(_Stack("/setcmykcolor where {pop 1} {2} ifelse", 1)[0], Is.EqualTo(1));
      Assert.That(_Stack("/nosuchname where {pop 1} {2} ifelse", 1)[0], Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Dictionaries_AreSearchedFromTheTopDown() {
    Assert.That(_Stack("/x 1 def 5 dict begin /x 2 def x end x add", 1)[0], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Bind_KeepsTheOperatorAfterTheNameIsRedefined() {
    Assert.That(_Stack("/plus {add} bind def /add {sub} def 10 4 plus", 1)[0], Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void Strings_SearchAndConvertTheWayTheReferenceStates() {
    Assert.Multiple(() => {
      // search leaves the part before the match, the match, and the part after it, in that order
      // from the top down, and says whether it found anything.
      Assert.That(_Stack("(abcde) (cd) search {length exch pop exch pop} {pop 99} ifelse", 1)[0], Is.EqualTo(2));
      Assert.That(_Stack("(abcde) (cd) search {pop exch pop length} {pop 99} ifelse", 1)[0], Is.EqualTo(2));
      Assert.That(_Stack("(abcde) (cd) search {pop pop length} {pop 99} ifelse", 1)[0], Is.EqualTo(1));
      Assert.That(_Stack("(abcde) (zz) search {pop pop length} {length} ifelse", 1)[0], Is.EqualTo(5));
      Assert.That(_Stack("(12.5) cvr", 1)[0], Is.EqualTo(12.5));
      Assert.That(_Stack("<414243> length", 1)[0], Is.EqualTo(3));
      Assert.That(_Stack("16#FF", 1)[0], Is.EqualTo(255));
      Assert.That(_Stack("2#1010", 1)[0], Is.EqualTo(10));
    });
  }

  [Test]
  [Category("Unit")]
  public void GraphicsStateStack_IsSeparateFromTheOperandStack() {
    var canvas = new VectorCanvas(4, 4, Rgba32.White);
    var page = new PsPage(canvas, Matrix2D.Identity);
    var bytes = Encoding.Latin1.GetBytes("gsave 0.5 setgray grestore");
    var interpreter = new PostScriptInterpreter(bytes, 0, bytes.Length, page);
    interpreter.Run();

    Assert.Multiple(() => {
      Assert.That(interpreter.GraphicsDepth, Is.EqualTo(0));
      Assert.That(interpreter.Graphics.Colour, Is.EqualTo(Rgba32.Black));
    });
  }

  #endregion

  #region what gets drawn

  [Test]
  [Category("Unit")]
  public void Page_IsTheBoundingBoxAtNinetySixPixelsToTheInch() {
    var rendered = _Render("showpage\n");

    Assert.Multiple(() => {
      // A hundred points is a hundred seventy-seconds of an inch, which is 133.33 pixels.
      Assert.That(rendered.Image.Width, Is.EqualTo(133));
      Assert.That(rendered.Image.Height, Is.EqualTo(133));
      Assert.That(rendered.SizeSource, Is.EqualTo("%%BoundingBox"));
      Assert.That(rendered.PagesShown, Is.EqualTo(1));
      Assert.That(rendered.HasInk, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void NoBoundingBox_TakesTheDefaultLetterPageAndSaysSo() {
    var rendered = PostScriptRenderer.Render(_Read("%!PS-Adobe-3.0\n%%EndComments\nshowpage\n"));

    Assert.Multiple(() => {
      Assert.That(rendered.Image.Width, Is.EqualTo(816));
      Assert.That(rendered.Image.Height, Is.EqualTo(1056));
      Assert.That(rendered.SizeSource, Does.Contain("US Letter"));
    });
  }

  [Test]
  [Category("Unit")]
  public void HiResBoundingBox_IsUsedWhereThereIsNoOtherAndSaysSo() {
    var rendered = PostScriptRenderer.Render(_Read("%!PS-Adobe-3.0\n%%HiResBoundingBox: 0 0 72 36\n%%EndComments\nshowpage\n"));

    Assert.Multiple(() => {
      Assert.That(rendered.Image.Width, Is.EqualTo(96));
      Assert.That(rendered.Image.Height, Is.EqualTo(48));
      Assert.That(rendered.SizeSource, Is.EqualTo("%%HiResBoundingBox"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FilledTriangle_CoversTheHalfOfThePageItsCornersEnclose() {
    // Corners at (0,0), (100,0) and (100,100): half the page, filled black.
    var image = _Draw("newpath 0 0 moveto 100 0 lineto 100 100 lineto closepath fill showpage\n");

    Assert.Multiple(() => {
      Assert.That(_Coverage(image), Is.EqualTo(0.5).Within(0.01));

      // The triangle is the lower right half in PostScript's frame, which is the lower right half
      // of the picture too once y has been turned over.
      Assert.That(_At(image, 120, 120), Is.EqualTo((0, 0, 0)));
      Assert.That(_At(image, 12, 12), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FilledTriangle_TakesTheColourItWasGivenInEveryModel() {
    var red = _Draw("1 0 0 setrgbcolor newpath 0 0 moveto 100 0 lineto 100 100 lineto fill showpage\n");
    var cyan = _Draw("1 0 0 0 setcmykcolor newpath 0 0 moveto 100 0 lineto 100 100 lineto fill showpage\n");
    var grey = _Draw("0.5 setgray newpath 0 0 moveto 100 0 lineto 100 100 lineto fill showpage\n");

    Assert.Multiple(() => {
      Assert.That(_At(red, 120, 120), Is.EqualTo((255, 0, 0)));
      Assert.That(_At(cyan, 120, 120), Is.EqualTo((0, 255, 255)));
      Assert.That(_At(grey, 120, 120), Is.EqualTo((128, 128, 128)));
    });
  }

  [Test]
  [Category("Unit")]
  public void StrokedPath_WithADash_LeavesLessInkThanTheSameLineSolid() {
    const string line = "newpath 10 50 moveto 90 50 lineto 4 setlinewidth stroke showpage\n";
    var solid = _Draw(line);
    var dashed = _Draw("[4 4] 0 setdash " + line);

    Assert.Multiple(() => {
      Assert.That(_Coverage(solid), Is.GreaterThan(0));

      // Half on and half off: about half the ink, and never more.
      Assert.That(_Coverage(dashed), Is.LessThan(_Coverage(solid) * 0.75));
      Assert.That(_Coverage(dashed), Is.GreaterThan(_Coverage(solid) * 0.25));
    });
  }

  [Test]
  [Category("Unit")]
  public void StrokedPath_IsAsWideAsTheTransformMakesIt() {
    var thin = _Draw("newpath 10 50 moveto 90 50 lineto 2 setlinewidth stroke showpage\n");
    var scaled = _Draw("2 2 scale newpath 5 25 moveto 45 25 lineto 2 setlinewidth stroke showpage\n");

    // The same line under a doubling: twice as wide and the same length, so twice the ink. The
    // tolerance is what four sample rows to a pixel cost a horizontal edge, which is a quarter of a
    // pixel on a line under three pixels wide.
    var expected = _Coverage(thin) * 2;
    Assert.That(_Coverage(scaled), Is.EqualTo(expected).Within(expected * 0.2));
  }

  [Test]
  [Category("Unit")]
  public void GsaveAndGrestore_PutEverythingBackIncludingThePath() {
    // Colour, transform, line width and the half-built path all belong to the graphics state, so a
    // save and a restore around a change must leave a picture identical to one without the change.
    var plain = _Draw("newpath 0 0 moveto 100 0 lineto 100 100 lineto fill showpage\n");
    var wrapped = _Draw(
      "newpath 0 0 moveto\n" +
      "gsave 1 0 0 setrgbcolor 50 50 translate 3 3 scale 9 setlinewidth 20 20 moveto 30 30 lineto grestore\n" +
      "100 0 lineto 100 100 lineto fill showpage\n"
    );

    Assert.That(wrapped.PixelData, Is.EqualTo(plain.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Grestore_BringsTheClipBack() {
    var clipped = _Draw(
      "gsave newpath 0 0 moveto 50 0 lineto 50 50 lineto 0 50 lineto closepath clip newpath\n" +
      "0 0 100 100 rectfill grestore\n" +
      "0 0 100 100 rectfill showpage\n"
    );

    // The second fill is outside the clip that the first one was made under, so the whole page is
    // inked rather than the quarter the clip allowed.
    Assert.That(_Coverage(clipped), Is.EqualTo(1).Within(0.01));
  }

  [Test]
  [Category("Unit")]
  public void Clip_ConfinesWhatComesAfterIt() {
    var image = _Draw(
      "newpath 0 0 moveto 50 0 lineto 50 50 lineto 0 50 lineto closepath clip newpath\n" +
      "0 0 100 100 rectfill showpage\n"
    );

    Assert.That(_Coverage(image), Is.EqualTo(0.25).Within(0.02));
  }

  /// <summary>A five-pointed star drawn as one self-crossing loop, centred on the page.</summary>
  private static string _Star() {
    var program = new StringBuilder("newpath\n");
    for (var i = 0; i < 5; ++i) {
      var angle = Math.PI / 2 + i * 4 * Math.PI / 5;
      var x = 50 + 45 * Math.Cos(angle);
      var y = 50 + 45 * Math.Sin(angle);
      program.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x:F4} {y:F4} {(i == 0 ? "moveto" : "lineto")}\n");
    }

    return program.Append("closepath\n").ToString();
  }

  [Test]
  [Category("Unit")]
  public void EofillAndFill_DifferByThePentagonInTheMiddleOfAStar() {
    // The five points of the star wind twice round its middle. The non-zero rule fills that middle
    // and the even-odd rule leaves it as a hole, and the hole is the whole of the difference.
    var nonZero = _Draw(_Star() + "fill showpage\n");
    var evenOdd = _Draw(_Star() + "eofill showpage\n");

    Assert.Multiple(() => {
      Assert.That(_Coverage(evenOdd), Is.LessThan(_Coverage(nonZero)));
      Assert.That(_At(nonZero, 66, 66), Is.EqualTo((0, 0, 0)));
      Assert.That(_At(evenOdd, 66, 66), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void Arc_ComesOutRoundAndTheRightSize() {
    var image = _Draw("newpath 50 50 40 0 360 arc fill showpage\n");

    // A circle of radius 40 in a square of side 100 covers pi times 40 squared over 100 squared.
    Assert.That(_Coverage(image), Is.EqualTo(Math.PI * 40 * 40 / 10000).Within(0.01));
  }

  [Test]
  [Category("Unit")]
  public void Curveto_BendsTowardsItsControlPoints() {
    var straight = _Draw("newpath 10 10 moveto 90 10 lineto 90 90 lineto closepath fill showpage\n");
    var bent = _Draw("newpath 10 10 moveto 90 10 90 10 90 90 curveto closepath fill showpage\n");

    // A curve whose two control points sit on the corner cuts that corner off, so it can only ever
    // enclose less than the straight path through the same corner.
    Assert.That(_Coverage(bent), Is.LessThan(_Coverage(straight)));
    Assert.That(_Coverage(bent), Is.GreaterThan(_Coverage(straight) * 0.5));
  }

  [Test]
  [Category("Unit")]
  public void Transform_AppliesToThePointsAddedAfterIt() {
    var moved = _Draw("50 50 translate newpath 0 0 moveto 50 0 lineto 50 50 lineto closepath fill showpage\n");
    var placed = _Draw("newpath 50 50 moveto 100 50 lineto 100 100 lineto closepath fill showpage\n");

    Assert.That(moved.PixelData, Is.EqualTo(placed.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Image_LandsInTheUnitSquareTheTransformPutsItIn() {
    // Four samples, black and white in a chequer, filling the left half of the page.
    var image = _Draw(
      "gsave 0 0 translate 50 100 scale\n" +
      "2 2 8 [2 0 0 -2 0 2] <00FFFF00> image grestore showpage\n"
    );

    Assert.Multiple(() => {
      Assert.That(_At(image, 12, 12), Is.EqualTo((0, 0, 0)));
      Assert.That(_At(image, 50, 12), Is.EqualTo((255, 255, 255)));
      Assert.That(_At(image, 12, 100), Is.EqualTo((255, 255, 255)));
      Assert.That(_At(image, 50, 100), Is.EqualTo((0, 0, 0)));

      // Nothing outside the square it was put in.
      Assert.That(_At(image, 100, 66), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ImageMask_PaintsTheCurrentColourWhereItsSamplesSayAndNowhereElse() {
    var image = _Draw(
      "1 0 0 setrgbcolor gsave 0 0 translate 100 100 scale\n" +
      "2 1 false [2 0 0 -1 0 1] <40> imagemask grestore showpage\n"
    );

    Assert.Multiple(() => {
      Assert.That(_At(image, 12, 66), Is.EqualTo((255, 0, 0)));
      Assert.That(_At(image, 100, 66), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ImageDataFromTheProgramsOwnText_IsReadFromWhereTheScannerStopped() {
    // The pattern every file that carries a raster uses: currentfile handed back to the program so
    // that the data written into it can be read out again.
    var image = _Draw(
      "/row 2 string def\n" +
      "gsave 0 0 translate 100 100 scale\n" +
      "2 2 8 [2 0 0 -2 0 2] {currentfile row readhexstring pop} image\n" +
      "00FF\nFF00\n" +
      "grestore showpage\n"
    );

    Assert.Multiple(() => {
      Assert.That(_At(image, 12, 12), Is.EqualTo((0, 0, 0)));
      Assert.That(_At(image, 100, 12), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void HexadecimalFilter_StopsWhereItsDataStopsAndLetsTheProgramCarryOn() {
    // A filter that read eagerly would swallow the operators after the data and refuse the file.
    var image = _Draw(
      "gsave 0 0 translate 100 100 scale\n" +
      "2 1 8 [2 0 0 -1 0 1] currentfile /ASCIIHexDecode filter image\n" +
      "00FF>\n" +
      "grestore showpage\n"
    );

    Assert.Multiple(() => {
      Assert.That(_At(image, 12, 66), Is.EqualTo((0, 0, 0)));
      Assert.That(_At(image, 100, 66), Is.EqualTo((255, 255, 255)));
    });
  }

  [Test]
  [Category("Unit")]
  public void Showpage_EndsThePageSoTheSecondOneIsNotDrawnOverTheFirst() {
    var rendered = _Render("newpath 0 0 moveto 100 0 lineto 100 100 lineto fill showpage 0 0 100 100 rectfill showpage\n");

    Assert.Multiple(() => {
      Assert.That(rendered.PagesShown, Is.EqualTo(1));
      Assert.That(_Coverage(rendered.Image), Is.EqualTo(0.5).Within(0.01));
    });
  }

  [Test]
  [Category("Unit")]
  public void Text_IsConsumedAndNotDrawn() {
    // The glyphs are in a font the file does not carry. A box where the words are would be geometry
    // the file never stated, so nothing is drawn and nothing is left on the stack either.
    var rendered = _Render("/Helvetica findfont 24 scalefont setfont 10 50 moveto (hello) show showpage\n");

    Assert.Multiple(() => {
      Assert.That(rendered.HasInk, Is.False);
      Assert.That(_Coverage(rendered.Image), Is.EqualTo(0).Within(1e-9));
    });
  }

  #endregion

  #region what is refused

  [Test]
  [Category("Unit")]
  public void BadToken_IsRefusedRatherThanPassedOver() {
    Assert.Multiple(() => {
      // A brace that closes nothing, a string that never closes, a hexadecimal string holding a
      // character that is not a digit: three ways of writing something that is not a program.
      Assert.Throws<InvalidDataException>(() => _Draw("0 0 moveto } fill showpage\n"));
      Assert.Throws<InvalidDataException>(() => _Draw("(never closed\n"));
      Assert.Throws<InvalidDataException>(() => _Draw("<0011zz> pop\n"));
      Assert.Throws<InvalidDataException>(() => _Draw("0 0 moveto ) fill\n"));
    });
  }

  [Test]
  [Category("Unit")]
  public void UndefinedOperator_StopsTheRenderRatherThanBeingSkipped() {
    // The failure this reader exists to avoid: an operator nobody implemented passed over, its
    // operands left behind, and the next figure painted in whatever colour they happened to make.
    var failure = Assert.Throws<InvalidDataException>(() => _Draw("1 0 0 setrgbcolor 0.5 setnosuchthing 0 0 100 100 rectfill showpage\n"));
    Assert.That(failure!.Message, Does.Contain("setnosuchthing"));
  }

  [Test]
  [Category("Unit")]
  public void ColourSpaceThatNeedsALookup_IsRefusedByName() {
    // An indexed or separation space needs a table or a profile to become ink. Guessing at it puts
    // a colour on the page that the file never asked for.
    var failure = Assert.Throws<InvalidDataException>(() => _Draw("[/Separation /Spot /DeviceCMYK {}] setcolorspace 0 0 100 100 rectfill showpage\n"));
    Assert.That(failure!.Message, Does.Contain("Separation"));

    // A CIE space has three components that are not red, green and blue. Taking them for those
    // would come out looking plausible and wrong, which is the worst of the two failures.
    Assert.Throws<InvalidDataException>(() => _Draw("[/CIEBasedABC << >>] setcolorspace 0 0 100 100 rectfill showpage\n"));
  }

  [Test]
  [Category("Unit")]
  public void TransferFunctionThatChangesThePicture_IsRefusedRatherThanIgnored() {
    Assert.Multiple(() => {
      // The identity is what a program installs to put the device back as it found it.
      Assert.DoesNotThrow(() => _Draw("{} settransfer 0 0 100 100 rectfill showpage\n"));
      Assert.Throws<InvalidDataException>(() => _Draw("{1 exch sub} settransfer 0 0 100 100 rectfill showpage\n"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Shading_IsRefusedBecauseTheRampCannotBeEvaluated() {
    Assert.Throws<InvalidDataException>(() => _Draw("<< /ShadingType 2 >> shfill showpage\n"));
  }

  [Test]
  [Category("Unit")]
  public void FileWithoutTheOpeningMark_IsNotAProgram() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => PostScriptReader.FromBytes(Encoding.ASCII.GetBytes("not postscript at all")));
      Assert.Throws<InvalidDataException>(() => PostScriptReader.FromBytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
    });
  }

  [Test]
  [Category("Unit")]
  public void PdfUnderAPostScriptName_GoesToThePdfReaderInstead() {
    var failure = Assert.Throws<InvalidDataException>(() => PostScriptReader.FromBytes(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n")));
    Assert.That(failure!.Message, Does.Contain("PDF"));
  }

  [Test]
  [Category("Unit")]
  public void ProcedureSetTheFileDoesNotCarry_IsRefusedByName() {
    // What the header says it needs and what the body carries are two different statements, and the
    // second is the one that can be checked.
    var needing = "%!PS-Adobe-3.0\n%%BoundingBox: 0 0 100 100\n%%DocumentNeededResources: procset Adobe_level2_AI5 1.0 0\n%%EndComments\n0 0 100 100 rectfill showpage\n";
    var failure = Assert.Throws<InvalidDataException>(() => PostScriptFile.ToRawImage(_Read(needing)));
    Assert.That(failure!.Message, Does.Contain("Adobe_level2_AI5"));

    var carrying = "%!PS-Adobe-3.0\n%%BoundingBox: 0 0 100 100\n%%DocumentNeededResources: procset Adobe_level2_AI5 1.0 0\n%%EndComments\n%%BeginProcSet: Adobe_level2_AI5 1.0 0\n%%EndProcSet\n0 0 100 100 rectfill showpage\n";
    Assert.DoesNotThrow(() => PostScriptFile.ToRawImage(_Read(carrying)));
  }

  [Test]
  [Category("Unit")]
  public void RunawayLoop_IsStoppedRatherThanRunForever() {
    Assert.Throws<InvalidDataException>(() => _Draw("{1 pop} loop showpage\n"));
  }

  #endregion

  #region what the format says about itself

  [Test]
  [Category("Unit")]
  public void ClaimedNames_IncludeTheOnesAPostScriptSpoolIsSavedUnder() {
    var extensions = _Extensions<PostScriptFile>();

    Assert.Multiple(() => {
      Assert.That(extensions, Does.Contain(".ps"));
      Assert.That(extensions, Does.Contain(".ps1"));
      Assert.That(extensions, Does.Contain(".ps2"));
      Assert.That(extensions, Does.Contain(".ps3"));
      Assert.That(extensions, Does.Contain(".eps"));
      Assert.That(extensions, Does.Contain(".prn"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Signature_IsTheTwoCharactersEveryProgramOpensWith() {
    Assert.Multiple(() => {
      Assert.That(_Matches<PostScriptFile>("%!PS-Adobe-3.0"u8), Is.True);
      Assert.That(_Matches<PostScriptFile>("GIF89a"u8), Is.False);

      // The four bytes of a file wrapped for a PC belong to the reader that takes the preview out
      // of one, so this has no opinion rather than an argument.
      Assert.That(_Matches<PostScriptFile>([0xC5, 0xD0, 0xD3, 0xC6]), Is.Null);
    });
  }

  private static string[] _Extensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  private static bool? _Matches<T>(ReadOnlySpan<byte> header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);

  #endregion
}
