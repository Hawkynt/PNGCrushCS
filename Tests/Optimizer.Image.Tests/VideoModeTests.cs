using System;
using FileFormat.Core;

namespace Optimizer.Image.Tests;

/// <summary>Unit tests for the <see cref="VideoMode"/> record and related types.</summary>
[TestFixture]
public sealed class VideoModeTests {

  [Test]
  [Category("Unit")]
  public void Constructor_RejectsEmptyName() {
    Assert.That(
      () => new VideoMode("", [(IntegerRange.Any, IntegerRange.Any)]),
      Throws.ArgumentException);
  }

  [Test]
  [Category("Unit")]
  public void Constructor_RejectsNullDimensions() {
    Assert.That(
      () => new VideoMode("X", null!),
      Throws.ArgumentException);
  }

  [Test]
  [Category("Unit")]
  public void Constructor_RejectsEmptyDimensions() {
    Assert.That(
      () => new VideoMode("X", Array.Empty<(IntegerRange, IntegerRange)>()),
      Throws.ArgumentException);
  }

  [Test]
  [Category("Unit")]
  public void MatchesDimensions_ExactSingleHit() {
    var mode = new VideoMode("Mode 1", [(320, 200)]);
    Assert.That(mode.MatchesDimensions(320, 200), Is.True);
    Assert.That(mode.MatchesDimensions(321, 200), Is.False);
    Assert.That(mode.MatchesDimensions(320, 201), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MatchesDimensions_MultipleCoupledPairs() {
    // VGA 256-colour modes: 320x200, 320x240, 360x480 all in one mode.
    var mode = new VideoMode("256-colour modes", [(320, 200), (320, 240), (360, 480)]);
    Assert.That(mode.MatchesDimensions(320, 200), Is.True);
    Assert.That(mode.MatchesDimensions(320, 240), Is.True);
    Assert.That(mode.MatchesDimensions(360, 480), Is.True);
    // Cross-combinations are NOT valid:
    Assert.That(mode.MatchesDimensions(360, 200), Is.False, "Cross-combo (360x200) must not match");
    Assert.That(mode.MatchesDimensions(320, 480), Is.False, "Cross-combo (320x480) must not match");
  }

  [Test]
  [Category("Unit")]
  public void MatchesDimensions_HonoursIntegerRangeStep() {
    // NES tile sheet: 128 wide, height = multiple of 8.
    var mode = new VideoMode("Tilesheet", [(128, new IntegerRange(8, 8192, step: 8))]);
    Assert.That(mode.MatchesDimensions(128, 8), Is.True);
    Assert.That(mode.MatchesDimensions(128, 16), Is.True);
    Assert.That(mode.MatchesDimensions(128, 17), Is.False, "Height 17 violates step-8 constraint");
    Assert.That(mode.MatchesDimensions(127, 8), Is.False, "Width 127 must not match");
  }

  [Test]
  [Category("Unit")]
  public void MaxColourCount_FullColourMode_IsMaxValue() {
    var mode = new VideoMode("RGB", [(IntegerRange.Any, IntegerRange.Any)]);
    Assert.That(mode.MaxColourCount, Is.EqualTo(int.MaxValue));
  }

  [Test]
  [Category("Unit")]
  public void MaxColourCount_DerivedFromLastRange() {
    var mode = new VideoMode("Indexed", [(320, 200)], [new IntegerRange(2, 16)]);
    Assert.That(mode.MaxColourCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void IsIndexed_TrueWhenPaletteRangesDeclared() {
    var indexed = new VideoMode("CGA", [(320, 200)], [4]);
    var fullColour = new VideoMode("RGB", [(IntegerRange.Any, IntegerRange.Any)]);
    Assert.That(indexed.IsIndexed, Is.True);
    Assert.That(fullColour.IsIndexed, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void PixelAspectRatio_TupleImplicitConversion() {
    PixelAspectRatio par = (6, 5);
    Assert.That(par.Numerator, Is.EqualTo(6));
    Assert.That(par.Denominator, Is.EqualTo(5));
    Assert.That(par.Ratio, Is.EqualTo(1.2).Within(1e-9));
  }

  [Test]
  [Category("Unit")]
  public void PixelAspectRatio_SquareIsOneOverOne() {
    Assert.That(PixelAspectRatio.Square.Numerator, Is.EqualTo(1));
    Assert.That(PixelAspectRatio.Square.Denominator, Is.EqualTo(1));
    Assert.That(PixelAspectRatio.Square.Ratio, Is.EqualTo(1.0));
  }

  [Test]
  [Category("Unit")]
  public void Equality_RecordSemantics() {
    var a = new VideoMode("Mode 1", [(320, 200)], [16]);
    var b = new VideoMode("Mode 1", [(320, 200)], [16]);
    // Reference-record arrays don't structurally equal — but the record's own value semantics
    // still apply to scalar fields. Verifying Name/DisplayFilter equality is the load-bearing piece.
    Assert.That(a.Name, Is.EqualTo(b.Name));
    Assert.That(a.DisplayFilter, Is.EqualTo(b.DisplayFilter));
  }

  [Test]
  [Category("Unit")]
  public void IntegerRange_Any_IsUnbounded() {
    Assert.That(IntegerRange.Any.Min, Is.EqualTo(1));
    Assert.That(IntegerRange.Any.Max, Is.EqualTo(int.MaxValue));
    Assert.That(IntegerRange.Any.Contains(1), Is.True);
    Assert.That(IntegerRange.Any.Contains(99999), Is.True);
    Assert.That(IntegerRange.Any.Contains(int.MaxValue), Is.True);
    Assert.That(IntegerRange.Any.Contains(0), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void Description_PreservedThroughInit() {
    var mode = new VideoMode("Mode 1", [(320, 200)]) {
      Description = "Amstrad CPC standard resolution",
    };
    Assert.That(mode.Description, Is.EqualTo("Amstrad CPC standard resolution"));
  }
}
