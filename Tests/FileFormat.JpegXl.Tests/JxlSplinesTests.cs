using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlSplines"/> — the spline rendering layer for JPEG XL
/// VarDCT (ISO/IEC 18181-1 §G.11; libjxl <c>lib/jxl/splines.cc</c> +
/// <c>lib/jxl/splines.h</c>).
///
/// <para>First-wave coverage: the HasSplines=0 short-circuit (the common
/// path) and the no-op behaviour of <see cref="JxlSplines.Apply"/> when there
/// are no splines to draw. The full bitstream parse and 2D Gaussian splatting
/// are NotImplementedException stubs at this stage and are not exercised
/// here — a separate test wave will land alongside that implementation.</para>
/// </summary>
[TestFixture]
internal sealed class JxlSplinesTests {

  // -----------------------------------------------------------------------
  // ReadList — bitstream parse
  // -----------------------------------------------------------------------

  /// <summary>HasSplines=0 (the LSB of the first byte) means splines are
  /// disabled for this frame, and the decoder must return null without
  /// consuming any further bits.</summary>
  [Test]
  public void ReadList_HasSplinesFlagZero_ReturnsNull() {
    // Bit 0 of the first byte is the HasSplines flag (LSB-first per
    // JxlBitReader). 0xFE has its LSB cleared.
    var data = new byte[] { 0xFE };
    var reader = new JxlBitReader(data, 0);
    // The entropy decoder is unused on the HasSplines=0 path. We can't
    // construct one without a bitstream, so pass null and rely on the
    // short-circuit.
    var result = JxlSplines.ReadList(reader, entropy: null!);
    Assert.That(result, Is.Null);
  }

  /// <summary>After consuming a 0 HasSplines flag, the bit reader should be
  /// positioned exactly 1 bit into the stream so subsequent layers (e.g. the
  /// noise/patches headers) can decode against the same reader.</summary>
  [Test]
  public void ReadList_HasSplinesFlagZero_AdvancesReaderByOneBit() {
    var data = new byte[] { 0xFE, 0x00 };
    var reader = new JxlBitReader(data, 0);
    var bitsBefore = reader.BitsRead;
    _ = JxlSplines.ReadList(reader, entropy: null!);
    Assert.That(reader.BitsRead - bitsBefore, Is.EqualTo(1L));
  }

  /// <summary>HasSplines=1 triggers the unimplemented full parse, which
  /// must throw a NotImplementedException with a clear message rather than
  /// silently producing wrong data.</summary>
  [Test]
  public void ReadList_HasSplinesFlagOne_ThrowsNotImplemented() {
    // 0x01 has its LSB set.
    var data = new byte[] { 0x01 };
    var reader = new JxlBitReader(data, 0);
    Assert.Throws<NotImplementedException>(() =>
      JxlSplines.ReadList(reader, entropy: null!));
  }

  /// <summary>The standalone <c>ReadHasSplinesFlag</c> seam returns the
  /// LSB of the next bit without throwing on either value.</summary>
  [Test]
  public void ReadHasSplinesFlag_ReadsFlagWithoutThrowing() {
    var data0 = new byte[] { 0x00 };
    var data1 = new byte[] { 0x01 };
    var r0 = new JxlBitReader(data0, 0);
    var r1 = new JxlBitReader(data1, 0);
    Assert.That(JxlSplines.ReadHasSplinesFlag(r0), Is.False);
    Assert.That(JxlSplines.ReadHasSplinesFlag(r1), Is.True);
  }

  /// <summary>Null reader is a programmer error.</summary>
  [Test]
  public void ReadList_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlSplines.ReadList(reader: null!, entropy: null!));
  }

  /// <summary>Null reader on the standalone flag seam is also rejected.</summary>
  [Test]
  public void ReadHasSplinesFlag_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlSplines.ReadHasSplinesFlag(reader: null!));
  }

  // -----------------------------------------------------------------------
  // Apply — rasterization
  // -----------------------------------------------------------------------

  /// <summary>An empty SplineList must leave the input planes untouched —
  /// this is the no-op contract for the first-wave VarDCT pipeline that
  /// hasn't yet emitted any splines from the bitstream.</summary>
  [Test]
  public void Apply_EmptySplineList_DoesNotMutateInput() {
    const int width = 8;
    const int height = 8;
    var planes = new float[3][];
    for (var c = 0; c < 3; c++) {
      planes[c] = new float[width * height];
      for (var i = 0; i < planes[c].Length; i++)
        planes[c][i] = (c + 1) * 0.125f * i;
    }
    var snapshot = new float[3][];
    for (var c = 0; c < 3; c++) {
      snapshot[c] = new float[planes[c].Length];
      Array.Copy(planes[c], snapshot[c], planes[c].Length);
    }
    var emptyList = new SplineList { Splines = [] };

    JxlSplines.Apply(planes, width, height, emptyList);

    for (var c = 0; c < 3; c++)
      Assert.That(planes[c], Is.EqualTo(snapshot[c]).AsCollection,
        $"Channel {c} must be untouched by an empty Apply.");
  }

  /// <summary>A null SplineList is also a no-op (defensive: callers may pass
  /// the result of <see cref="JxlSplines.ReadList"/> straight through).</summary>
  [Test]
  public void Apply_NullSplineList_DoesNotMutateInput() {
    var planes = new float[][] {
      new float[] { 1.0f, 2.0f, 3.0f, 4.0f },
      new float[] { 5.0f, 6.0f, 7.0f, 8.0f },
      new float[] { 9.0f, 10.0f, 11.0f, 12.0f }
    };
    JxlSplines.Apply(planes, 2, 2, splineList: null!);
    Assert.That(planes[0], Is.EqualTo(new[] { 1.0f, 2.0f, 3.0f, 4.0f }).AsCollection);
    Assert.That(planes[1], Is.EqualTo(new[] { 5.0f, 6.0f, 7.0f, 8.0f }).AsCollection);
    Assert.That(planes[2], Is.EqualTo(new[] { 9.0f, 10.0f, 11.0f, 12.0f }).AsCollection);
  }

  /// <summary>A non-empty SplineList triggers the unimplemented splatting
  /// path. We construct a minimal Spline (no control points, zero DCTs) just
  /// to populate the Splines array — the implementation must surface the
  /// gap rather than silently producing wrong pixels.</summary>
  [Test]
  public void Apply_NonEmptySplineList_ThrowsNotImplemented() {
    var spline = new Spline {
      ControlPoints = [new Point2D(0, 0), new Point2D(1, 1)],
      Dct32X = new float[32],
      Dct32Y = new float[32],
      Dct32B = new float[32],
      Dct32Sigma = new float[32]
    };
    var list = new SplineList { Splines = [spline] };
    var planes = new float[][] {
      new float[16], new float[16], new float[16]
    };
    Assert.Throws<NotImplementedException>(() =>
      JxlSplines.Apply(planes, 4, 4, list));
  }

  /// <summary>Channels array must have exactly 3 entries (XYB).</summary>
  [Test]
  public void Apply_WrongChannelCount_Throws() {
    var planes = new float[][] { new float[4], new float[4] };
    var emptyList = new SplineList { Splines = [] };
    Assert.Throws<ArgumentException>(() =>
      JxlSplines.Apply(planes, 2, 2, emptyList));
  }

  /// <summary>Each plane must have exactly width*height pixels.</summary>
  [Test]
  public void Apply_MismatchedPlaneSize_Throws() {
    var planes = new float[][] {
      new float[4],
      new float[3],   // wrong size
      new float[4]
    };
    var emptyList = new SplineList { Splines = [] };
    Assert.Throws<ArgumentException>(() =>
      JxlSplines.Apply(planes, 2, 2, emptyList));
  }

  /// <summary>Negative dimensions are rejected.</summary>
  [Test]
  public void Apply_NegativeDimensions_Throws() {
    var planes = new float[][] { [], [], [] };
    var emptyList = new SplineList { Splines = [] };
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlSplines.Apply(planes, -1, 0, emptyList));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlSplines.Apply(planes, 0, -1, emptyList));
  }

  /// <summary>Null channels argument is rejected up front.</summary>
  [Test]
  public void Apply_NullChannels_Throws() {
    var emptyList = new SplineList { Splines = [] };
    Assert.Throws<ArgumentNullException>(() =>
      JxlSplines.Apply(channels: null!, 2, 2, emptyList));
  }

  // -----------------------------------------------------------------------
  // Data types
  // -----------------------------------------------------------------------

  /// <summary>Point2D should behave like a value-equality record struct.</summary>
  [Test]
  public void Point2D_RecordStructEquality() {
    var a = new Point2D(3, 7);
    var b = new Point2D(3, 7);
    var c = new Point2D(3, 8);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a == b, Is.True);
    Assert.That(a, Is.Not.EqualTo(c));
    Assert.That(a != c, Is.True);
    Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    Assert.That(a.X, Is.EqualTo(3));
    Assert.That(a.Y, Is.EqualTo(7));
  }

  /// <summary>Default Point2D is (0, 0).</summary>
  [Test]
  public void Point2D_Default_IsOrigin() {
    var p = default(Point2D);
    Assert.That(p.X, Is.EqualTo(0));
    Assert.That(p.Y, Is.EqualTo(0));
  }

  /// <summary>Point2D supports the with-expression copy semantics that come
  /// for free with record structs.</summary>
  [Test]
  public void Point2D_WithExpression_ProducesModifiedCopy() {
    var p = new Point2D(10, 20);
    var q = p with { X = 99 };
    Assert.That(q, Is.EqualTo(new Point2D(99, 20)));
    Assert.That(p, Is.EqualTo(new Point2D(10, 20))); // original unchanged
  }

  /// <summary>SplineList has a sensible default-empty Splines collection.
  /// </summary>
  [Test]
  public void SplineList_DefaultsToEmptySplines() {
    var list = new SplineList();
    Assert.That(list.Splines, Is.Not.Null);
    Assert.That(list.Splines, Has.Length.EqualTo(0));
  }

  /// <summary>Spline defaults to empty arrays for all four DCT vectors and
  /// the control-point list.</summary>
  [Test]
  public void Spline_DefaultsToEmptyArrays() {
    var s = new Spline();
    Assert.That(s.ControlPoints, Is.Not.Null.And.Empty);
    Assert.That(s.Dct32X, Is.Not.Null.And.Empty);
    Assert.That(s.Dct32Y, Is.Not.Null.And.Empty);
    Assert.That(s.Dct32B, Is.Not.Null.And.Empty);
    Assert.That(s.Dct32Sigma, Is.Not.Null.And.Empty);
  }

  /// <summary>Spline init-only properties accept caller-supplied arrays
  /// verbatim (no defensive copy at construction).</summary>
  [Test]
  public void Spline_StoresProvidedArrays() {
    var pts = new Point2D[] { new(0, 0), new(5, 5), new(10, 0) };
    var dctX = new float[32];
    dctX[0] = 1.5f;
    var s = new Spline {
      ControlPoints = pts,
      Dct32X = dctX,
      Dct32Y = new float[32],
      Dct32B = new float[32],
      Dct32Sigma = new float[32]
    };
    Assert.That(s.ControlPoints, Is.SameAs(pts));
    Assert.That(s.Dct32X, Is.SameAs(dctX));
    Assert.That(s.Dct32X[0], Is.EqualTo(1.5f));
    Assert.That(s.Dct32Y.Length, Is.EqualTo(32));
    Assert.That(s.Dct32B.Length, Is.EqualTo(32));
    Assert.That(s.Dct32Sigma.Length, Is.EqualTo(32));
  }
}
