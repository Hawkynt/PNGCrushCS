using System;
using System.IO;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The splines layer (ISO/IEC 18181-1 §G.11; libjxl <c>lib/jxl/splines.cc</c>).
/// </summary>
/// <remarks>
/// What used to be here tested a stub: it asserted that a frame with splines
/// threw, and that a one-bit flag at the head of the section said whether there
/// were any. There is no such flag — a frame states splines in its header flags
/// and the section starts straight in on its entropy histograms — so the test
/// was pinning an invention rather than the format.
///
/// <para>The fixture is 2,048 pixels square and 81 bytes long, which is only
/// possible because the picture is nothing but splines. That makes it the right
/// thing to measure against <c>djxl</c>: nearly every sample in it came out of
/// this code.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlSplinesTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>Where the reference window sits in the full picture.</summary>
  private const int _WindowX = 0;
  private const int _WindowY = 256;
  private const int _WindowSide = 256;

  /// <summary>
  /// A picture made of splines decodes to what libjxl decodes it to.
  /// </summary>
  /// <remarks>
  /// The reference is a 256-pixel window rather than the whole 2,048 square,
  /// because the whole one is twelve megabytes and the largest fixture here is
  /// a fifth of a megabyte. The window is the busiest square in the picture,
  /// which is what makes it worth keeping: it crosses several splines and
  /// their faint outer edges, and it is those edges that caught the cutoff
  /// radius being the fast one rather than the accurate one.
  ///
  /// <para>The tolerance is the one the lossy files get, for the same reason:
  /// <c>djxl</c> dithers its eight-bit output. Measured before that rounding,
  /// against libjxl's own float decode, the full picture differs in 52 samples
  /// of 12,582,912 and every one of them sits on a rounding boundary.</para>
  /// </remarks>
  [Test]
  public void APictureOfSplinesDecodesToWhatLibjxlDecodesItTo() {
    var file = JpegXlReader.FromBytes(_Fixture("splines.jxl"));
    var (side, sideAgain, expected) = _ReadPpm(_Fixture("splines_window.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2048));
      Assert.That(file.Height, Is.EqualTo(2048));
      Assert.That(file.ComponentCount, Is.EqualTo(3));
      Assert.That(side, Is.EqualTo(_WindowSide));
      Assert.That(sideAgain, Is.EqualTo(_WindowSide));
    });

    var worst = 0;
    var differing = 0;
    for (var y = 0; y < _WindowSide; ++y)
    for (var x = 0; x < _WindowSide; ++x)
    for (var c = 0; c < 3; ++c) {
      var mine = file.PixelData[(((_WindowY + y) * file.Width) + _WindowX + x) * 3 + c];
      var theirs = expected[(y * _WindowSide + x) * 3 + c];
      var apart = Math.Abs(mine - theirs);
      if (apart == 0)
        continue;
      ++differing;
      worst = Math.Max(worst, apart);
    }

    Assert.Multiple(() => {
      Assert.That(worst, Is.LessThanOrEqualTo(1),
        $"a sample is out by {worst} levels, which is more than libjxl's output dither can explain");
      Assert.That(differing, Is.LessThan(_WindowSide * _WindowSide * 3 / 2));
    });
  }

  /// <summary>
  /// The section is entropy-coded from its first bit, so a truncated one has to
  /// be refused rather than read as an empty list.
  /// </summary>
  [Test]
  public void ATruncatedSplineSectionIsRefused() {
    var reader = new JxlBitReader([0x00, 0x00], 0);
    Assert.That(
      () => JxlSplines.Decode(reader, 64 * 64),
      Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<InvalidOperationException>()
        .Or.InstanceOf<ArgumentOutOfRangeException>());
  }

  /// <summary>
  /// The colour a spline states is relative to the luma channel, and the B half
  /// of that relation starts at one rather than at nothing.
  /// </summary>
  /// <remarks>
  /// Passing zero there is not a small error: it drops the whole Y contribution
  /// out of the blue channel. On the fixture it left blue wrong by up to 133
  /// levels while red and green were already exact, which is what makes this
  /// worth its own test rather than only the end-to-end one.
  /// </remarks>
  [Test]
  public void TheBlueChannelTakesTheLumaCurveOnTopOfItsOwn() {
    var list = new SplineList {
      StartingPoints = [new Point2D(8, 8)],
      QuantizationAdjustment = 0,
      Quantized = [
        new QuantizedSpline {
          ControlPointDeltas = [(4, 0), (4, 0)],
          // Y alone: the other two channels state nothing of their own, so
          // whatever shows up in them came from the correlation.
          ColorDct = [new int[32], _Dc(2000), new int[32]],
          SigmaDct = _Dc(60),
        },
      ],
    };

    var withB = JxlSplines.BuildSegments(list, 64, 64, yToX: 0.0f, yToB: 1.0f);
    var withoutB = JxlSplines.BuildSegments(list, 64, 64, yToX: 0.0f, yToB: 0.0f);

    Assert.That(withB, Is.Not.Empty);
    var lit = withB[withB.Count / 2];
    var unlit = withoutB[withoutB.Count / 2];

    Assert.Multiple(() => {
      Assert.That(lit.Color[1], Is.Not.EqualTo(0.0f), "the luma channel is what was stated");
      Assert.That(lit.Color[2], Is.EqualTo(lit.Color[1]).Within(1e-6f),
        "with a ratio of one the blue channel is the luma channel");
      Assert.That(unlit.Color[2], Is.EqualTo(0.0f), "with a ratio of nothing it is nothing");
      Assert.That(lit.Color[0], Is.EqualTo(0.0f), "the X ratio really does start at nothing");
    });
  }

  /// <summary>
  /// A spline that doubles back onto its own last point has no direction there,
  /// and the tessellation divides by the gap.
  /// </summary>
  [Test]
  public void ASplineThatRepeatsAControlPointIsRefused() {
    var list = new SplineList {
      StartingPoints = [new Point2D(8, 8)],
      QuantizationAdjustment = 0,
      Quantized = [
        new QuantizedSpline {
          // First step of (0,0) puts the second control point on the first.
          ControlPointDeltas = [(0, 0)],
          ColorDct = [_Dc(100), _Dc(100), _Dc(100)],
          SigmaDct = _Dc(60),
        },
      ],
    };

    Assert.That(() => JxlSplines.BuildSegments(list, 64, 64, 0.0f, 1.0f),
      Throws.InstanceOf<InvalidDataException>());
  }

  /// <summary>
  /// Drawing is confined to the picture; a spline that starts outside it still
  /// must not write outside the buffer.
  /// </summary>
  [Test]
  public void ASplineOutsideThePictureDrawsNothingOutsideTheBuffer() {
    var list = new SplineList {
      StartingPoints = [new Point2D(-400, -400)],
      QuantizationAdjustment = 0,
      Quantized = [
        new QuantizedSpline {
          ControlPointDeltas = [(2, 2), (2, 2)],
          ColorDct = [_Dc(500), _Dc(500), _Dc(500)],
          SigmaDct = _Dc(60),
        },
      ],
    };

    const int side = 32;
    var planes = new float[3][];
    for (var c = 0; c < 3; ++c)
      planes[c] = new float[side * side];

    var segments = JxlSplines.BuildSegments(list, side, side, 0.0f, 1.0f);
    Assert.That(() => JxlSplines.AddTo(segments, planes, side, side), Throws.Nothing);
    Assert.That(planes[1].All(v => v == 0.0f), Is.True, "nothing of it reaches the picture");
  }

  private static int[] _Dc(int value) {
    var dct = new int[32];
    dct[0] = value;
    return dct;
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
