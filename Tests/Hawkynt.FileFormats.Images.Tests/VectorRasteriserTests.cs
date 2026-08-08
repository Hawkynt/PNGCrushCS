using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Core.Vector.Tests;

/// <summary>The shared rasteriser, tested on shapes whose answers are known without it.</summary>
/// <remarks>
/// Every drawing format in this tree draws through these, so a fault here is a fault in all of
/// them at once and would show up as a picture that is subtly wrong rather than as an exception.
/// The shapes chosen are ones whose area or coverage can be worked out on paper: a triangle, a
/// five-pointed star whose middle is inside under one fill rule and outside under the other, and a
/// straight stroke whose covered area is its length times its width.
/// </remarks>
[TestFixture]
public sealed class VectorRasteriserTests {

  private const int _Size = 100;

  private static VectorCanvas _Blank() => new(_Size, _Size, Rgba32.White);

  /// <summary>How many pixels are not the background, and how much ink they hold in total.</summary>
  private static (int Count, double Ink) _Ink(VectorCanvas canvas) {
    var image = canvas.ToRawImage();
    var pixels = image.PixelData;
    var count = 0;
    var ink = 0.0;

    for (var i = 0; i < pixels.Length; i += 4) {
      // Black on white, so the ink at a pixel is how far its red channel has fallen from full.
      var darkness = (255 - pixels[i]) / 255.0;
      if (darkness <= 0)
        continue;

      ++count;
      ink += darkness;
    }

    return (count, ink);
  }

  /// <summary>A five-pointed star drawn as one self-crossing loop.</summary>
  private static VectorPath _Star(double centre, double radius) {
    var path = new VectorPath();
    for (var i = 0; i < 5; ++i) {
      // Every other vertex of a decagon, which is what makes the loop cross itself.
      var angle = -Math.PI / 2 + 2 * Math.PI * (i * 2 % 5) / 5;
      var (sin, cos) = Math.SinCos(angle);
      var x = centre + radius * cos;
      var y = centre + radius * sin;
      if (i == 0)
        path.MoveTo(x, y);
      else
        path.LineTo(x, y);
    }

    path.Close();
    return path;
  }

  [Test]
  [Category("Unit")]
  public void Fill_Triangle_CoversHalfTheSquareItSits() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.MoveTo(10, 10);
    path.LineTo(90, 10);
    path.LineTo(10, 90);
    path.Close();

    canvas.Fill(path, FillRule.NonZero, Rgba32.Black);

    // Half of an eighty by eighty square is 3200, and the sampling can only be out by the diagonal.
    var (_, ink) = _Ink(canvas);
    Assert.That(ink, Is.EqualTo(3200).Within(80), "a right triangle covers half its box");
  }

  [Test]
  [Category("Unit")]
  public void Fill_Triangle_LeavesTheOutsideAlone() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.MoveTo(10, 10);
    path.LineTo(90, 10);
    path.LineTo(10, 90);
    path.Close();

    canvas.Fill(path, FillRule.NonZero, Rgba32.Black);
    var pixels = canvas.ToRawImage().PixelData;

    Assert.Multiple(() => {
      Assert.That(pixels[(20 * _Size + 20) * 4], Is.EqualTo(0), "a point well inside is painted");
      Assert.That(pixels[(80 * _Size + 80) * 4], Is.EqualTo(255), "a point beyond the hypotenuse is not");
      Assert.That(pixels[(5 * _Size + 5) * 4], Is.EqualTo(255), "a point outside the box is not");
    });
  }

  [Test]
  [Category("Unit")]
  public void Fill_SelfIntersectingStar_TheTwoRulesDisagreeAboutItsMiddle() {
    var nonZero = _Blank();
    var evenOdd = _Blank();

    nonZero.Fill(_Star(50, 40), FillRule.NonZero, Rgba32.Black);
    evenOdd.Fill(_Star(50, 40), FillRule.EvenOdd, Rgba32.Black);

    var centreOfNonZero = nonZero.ToRawImage().PixelData[(50 * _Size + 50) * 4];
    var centreOfEvenOdd = evenOdd.ToRawImage().PixelData[(50 * _Size + 50) * 4];

    Assert.Multiple(() => {
      Assert.That(centreOfNonZero, Is.EqualTo(0), "the middle winds twice, so the non-zero rule fills it");
      Assert.That(centreOfEvenOdd, Is.EqualTo(255), "the middle is crossed twice, so the even-odd rule leaves it");
    });
  }

  [Test]
  [Category("Unit")]
  public void Fill_SelfIntersectingStar_NonZeroCoversMoreThanEvenOdd() {
    var nonZero = _Blank();
    var evenOdd = _Blank();

    nonZero.Fill(_Star(50, 40), FillRule.NonZero, Rgba32.Black);
    evenOdd.Fill(_Star(50, 40), FillRule.EvenOdd, Rgba32.Black);

    var (_, filled) = _Ink(nonZero);
    var (_, pierced) = _Ink(evenOdd);

    // The pentagon in the middle is what the two rules differ by, and for a star of this radius it
    // is a good fraction of the whole — enough that no rounding could account for it.
    Assert.That(filled - pierced, Is.GreaterThan(500), "the difference is the pentagon in the middle");
  }

  [Test]
  [Category("Unit")]
  public void Stroke_StraightLine_CoversItsLengthTimesItsWidth() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.MoveTo(20, 50);
    path.LineTo(80, 50);

    canvas.Stroke(path, 6, Rgba32.Black);

    var (_, ink) = _Ink(canvas);
    Assert.That(ink, Is.EqualTo(60 * 6).Within(12), "sixty long and six wide");
  }

  [Test]
  [Category("Unit")]
  public void Stroke_ZeroWidth_StillDrawsAHairline() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.MoveTo(20, 50);
    path.LineTo(80, 50);

    canvas.Stroke(path, 0, Rgba32.Black);

    var (_, ink) = _Ink(canvas);
    Assert.That(ink, Is.EqualTo(60).Within(6), "the thinnest line the surface can draw is one pixel");
  }

  [Test]
  [Category("Unit")]
  public void Stroke_ClosedSquare_DrawsFourSidesAndNotTheMiddle() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.AddRectangle(20, 20, 60, 60);

    canvas.Stroke(path, 4, Rgba32.Black);
    var pixels = canvas.ToRawImage().PixelData;

    Assert.Multiple(() => {
      Assert.That(pixels[(20 * _Size + 50) * 4], Is.EqualTo(0), "the top side is drawn");
      Assert.That(pixels[(80 * _Size + 50) * 4], Is.EqualTo(0), "the bottom side is drawn");
      Assert.That(pixels[(50 * _Size + 20) * 4], Is.EqualTo(0), "the left side is drawn");
      Assert.That(pixels[(50 * _Size + 80) * 4], Is.EqualTo(0), "the right side is drawn");
      Assert.That(pixels[(50 * _Size + 50) * 4], Is.EqualTo(255), "the middle is not");
    });
  }

  [Test]
  [Category("Unit")]
  public void Fill_ThroughAStipple_PaintsOnlyWhereThePatternIsSet() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.AddRectangle(10, 10, 80, 80);

    // Every other column: eight bits on, eight off, repeating across the sixteen-wide tile.
    canvas.Fill(path, FillRule.NonZero, Rgba32.Black, new VectorStipple([0xFF00]));

    var (_, ink) = _Ink(canvas);
    Assert.That(ink, Is.EqualTo(80 * 80 / 2).Within(120), "half the columns are painted");
  }

  [Test]
  [Category("Unit")]
  public void Fill_ThroughABlankStipple_PaintsNothing() {
    var canvas = _Blank();
    var path = new VectorPath();
    path.AddRectangle(10, 10, 80, 80);

    canvas.Fill(path, FillRule.NonZero, Rgba32.Black, new VectorStipple([0]));

    var (count, _) = _Ink(canvas);
    Assert.That(count, Is.Zero, "a pattern of all holes lets nothing through");
  }

  [Test]
  [Category("Unit")]
  public void Fill_ThroughAMask_IsConfinedToIt() {
    var canvas = _Blank();
    var clip = new VectorPath();
    clip.AddRectangle(0, 0, 50, _Size);
    var mask = canvas.MaskOf(clip, FillRule.NonZero);

    var path = new VectorPath();
    path.AddRectangle(10, 10, 80, 80);
    canvas.Fill(path, FillRule.NonZero, VectorPaint.Solid(Rgba32.Black), null, mask);

    var pixels = canvas.ToRawImage().PixelData;
    Assert.Multiple(() => {
      Assert.That(pixels[(50 * _Size + 30) * 4], Is.EqualTo(0), "inside the clip is painted");
      Assert.That(pixels[(50 * _Size + 70) * 4], Is.EqualTo(255), "outside it is not");
    });
  }

  [Test]
  [Category("Unit")]
  public void Dashed_HalvesTheInkOfAnEvenPattern() {
    var solid = _Blank();
    var dashed = _Blank();
    var path = new VectorPath();
    path.MoveTo(10, 50);
    path.LineTo(90, 50);

    solid.Stroke(path, 4, Rgba32.Black);
    dashed.Stroke(path.Dashed([8, 8]), 4, Rgba32.Black);

    var (_, whole) = _Ink(solid);
    var (_, broken) = _Ink(dashed);

    Assert.That(broken, Is.EqualTo(whole / 2).Within(whole * 0.15), "eight on and eight off draws half the line");
  }

  [Test]
  [Category("Unit")]
  public void CurveTo_IsFlattenedCloseEnoughToPassThroughItsMidpoint() {
    var path = new VectorPath();
    path.MoveTo(0, 0);
    path.CurveTo(0, 100, 100, 100, 100, 0);

    // At the halfway parameter a cubic sits at the average of its four control points weighted
    // one, three, three, one — here (50, 75).
    var points = path.SubPaths.Single();
    var closest = double.MaxValue;
    for (var i = 0; i < points.Xs.Length; ++i)
      closest = Math.Min(closest, Math.Abs(points.Xs.Span[i] - 50) + Math.Abs(points.Ys.Span[i] - 75));

    Assert.That(closest, Is.LessThan(1), "the flattened curve passes through the true midpoint");
  }

  [Test]
  [Category("Unit")]
  public void Matrix_Then_AppliesThisOneFirst() {
    var scaleThenMove = Matrix2D.Scaling(2, 2).Then(Matrix2D.Translation(10, 0));
    Assert.That(scaleThenMove.Apply(1, 0), Is.EqualTo((12.0, 0.0)), "scaled to two and then moved by ten");
  }

  [Test]
  [Category("Unit")]
  public void Viewport_Fit_TurnsTheDrawingOverWhenItsYPointsUp() {
    var viewport = VectorViewport.Fit(0, 0, 10, 10, 100, 100, true);

    Assert.Multiple(() => {
      Assert.That(viewport.Transform.Apply(0, 0).Y, Is.EqualTo(100).Within(1e-9), "the bottom of the drawing is the bottom row");
      Assert.That(viewport.Transform.Apply(0, 10).Y, Is.EqualTo(0).Within(1e-9), "the top of the drawing is the first row");
    });
  }

  [Test]
  [Category("Unit")]
  public void Canvas_RefusesASurfaceLargerThanItWillDraw()
    => Assert.Throws<ArgumentOutOfRangeException>(() => new VectorCanvas(65536, 65536, Rgba32.White));

  [Test]
  [Category("Unit")]
  public void Canvas_KeepsTheColourItWasClearedToWhereNothingWasDrawn() {
    var canvas = new VectorCanvas(4, 4, Rgba32.White with { A = 0 });
    var pixels = canvas.ToRawImage().PixelData;

    Assert.Multiple(() => {
      Assert.That(pixels[3], Is.Zero, "nothing was drawn, so nothing is opaque");
      Assert.That(pixels[0], Is.EqualTo(255), "and the colour under it is the one it was cleared to");
    });
  }
}
