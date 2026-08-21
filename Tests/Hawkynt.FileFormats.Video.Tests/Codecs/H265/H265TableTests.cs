using System;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The normative tables, checked against the properties they were designed to have.
/// </summary>
/// <remarks>
/// A table entered by hand from a standard is a place where a typo does not announce itself: the
/// decoder still runs, still consumes the right number of bits, and produces a picture that is
/// slightly or entirely wrong. Comparing a decode against a reference decoder catches that, and is
/// the real check — but only for the entries a given stream happens to reach. What these tests add is
/// the structure: the transform matrix is orthogonal or it is not one, the scan orders visit every
/// position exactly once or they are not permutations, and the arithmetic that turns an
/// initialisation value into a probability state stays inside the range the standard gives it for
/// every value and every quantiser, not just the ones a test stream used.
/// </remarks>
[TestFixture]
public sealed class H265TableTests {

  // ==============================================================================================
  // The transforms — clause 8.6.4.2
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void TheTransformMatrixIsOrthogonal() {
    var matrix = H265Transform.Matrix;

    // Each row against every other: the standard's matrix is an integer approximation of a scaled
    // orthonormal basis, so a row against itself is 64 squared times its length and a row against
    // any other is small. Exactly zero for most pairs, and never far from it — which is the property
    // a transposed digit destroys.
    for (var i = 0; i < 32; ++i)
      for (var j = 0; j < 32; ++j) {
        var sum = 0;
        for (var k = 0; k < 32; ++k)
          sum += matrix[(i << 5) + k] * matrix[(j << 5) + k];

        if (i == j)
          Assert.That(sum, Is.InRange(32 * 4000, 32 * 4200), $"row {i} against itself");
        else
          Assert.That(Math.Abs(sum), Is.LessThan(400), $"row {i} against row {j}");
      }
  }

  [Test]
  [Category("Unit")]
  public void EachTransformRowIsSymmetricOrAntisymmetricAboutItsMiddle() {
    var matrix = H265Transform.Matrix;

    for (var row = 0; row < 32; ++row) {
      var sign = (row & 1) == 0 ? 1 : -1;
      for (var column = 0; column < 16; ++column)
        Assert.That(
          matrix[(row << 5) + 31 - column], Is.EqualTo(sign * matrix[(row << 5) + column]),
          $"row {row}, column {column}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheSmallerTransformsAreTheLargersEverySecondFourthAndEighthRow() {
    var matrix = H265Transform.Matrix;

    // The 4x4 matrix, which is the one small enough to write down: its four rows are rows 0, 8, 16
    // and 24 of the tabulated one, truncated to four columns.
    int[] expected = [64, 64, 64, 64, 83, 36, -36, -83, 64, -64, -64, 64, 36, -83, 83, -36];

    for (var row = 0; row < 4; ++row)
      for (var column = 0; column < 4; ++column)
        Assert.That(matrix[((row * 8) << 5) + column], Is.EqualTo(expected[row * 4 + column]));
  }

  [Test]
  [Category("Unit")]
  public void TheSineTransformIsTheOneTheStandardTabulates() {
    int[] expected = [29, 55, 74, 84, 74, 74, 0, -74, 84, -29, -74, 55, 55, -84, 74, -29];

    for (var i = 0; i < 16; ++i)
      Assert.That(H265Transform.SineMatrix[i], Is.EqualTo(expected[i]), $"entry {i}");
  }

  [Test]
  [Category("Unit")]
  public void ADirectCurrentCoefficientAloneTransformsToAFlatBlock() {
    // The property every inverse transform must have, and the one that fails first when the matrix
    // is indexed the wrong way round: the lowest frequency alone is a constant across the block.
    foreach (var log2Size in new[] { 2, 3, 4, 5 }) {
      var size = 1 << log2Size;
      var block = new int[size * size];
      block[0] = 4096;

      H265Transform.Inverse(block, log2Size, false, 8);

      for (var i = 1; i < size * size; ++i)
        Assert.That(block[i], Is.EqualTo(block[0]), $"size {size}, position {i}");

      Assert.That(block[0], Is.Not.Zero, $"size {size}");
    }
  }

  // ==============================================================================================
  // The coefficient scans — clauses 6.5.3 to 6.5.5
  // ==============================================================================================

  [TestCase(H265ScanOrder.DIAGONAL)]
  [TestCase(H265ScanOrder.HORIZONTAL)]
  [TestCase(H265ScanOrder.VERTICAL)]
  [Category("Unit")]
  public void EveryScanVisitsEveryPositionExactlyOnce(int scanIdx) {
    for (var log2Size = 0; log2Size < 6; ++log2Size) {
      var size = 1 << log2Size;
      var scan = H265ScanOrder.Positions(log2Size, scanIdx);
      var seen = new bool[size * size];

      for (var i = 0; i < size * size; ++i) {
        var x = H265ScanOrder.X(scan, i);
        var y = H265ScanOrder.Y(scan, i);

        Assert.That(x, Is.LessThan(size));
        Assert.That(y, Is.LessThan(size));
        Assert.That(seen[y * size + x], Is.False, $"size {size} visits ({x},{y}) twice");
        seen[y * size + x] = true;
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void TheDiagonalScanWalksEachAntiDiagonalFromItsTop() {
    var scan = H265ScanOrder.Positions(2, H265ScanOrder.DIAGONAL);

    // The first six positions of a 4x4 block, which the standard's loop produces in this order.
    int[] expected = [0, 0, 0, 1, 1, 0, 0, 2, 1, 1, 2, 0];

    for (var i = 0; i < expected.Length; ++i)
      Assert.That(scan[i], Is.EqualTo(expected[i]), $"entry {i}");
  }

  [Test]
  [Category("Unit")]
  public void TheHorizontalAndVerticalScansAreEachOthersTranspose() {
    var horizontal = H265ScanOrder.Positions(3, H265ScanOrder.HORIZONTAL);
    var vertical = H265ScanOrder.Positions(3, H265ScanOrder.VERTICAL);

    for (var i = 0; i < 64; ++i) {
      Assert.That(H265ScanOrder.X(vertical, i), Is.EqualTo(H265ScanOrder.Y(horizontal, i)));
      Assert.That(H265ScanOrder.Y(vertical, i), Is.EqualTo(H265ScanOrder.X(horizontal, i)));
    }
  }

  // ==============================================================================================
  // The arithmetic decoder's context initialisation — clause 9.3.2.2
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryContextInitialisesToAStateInsideTheRange() {
    var states = new byte[H265CabacContexts.COUNT];

    for (var initType = 0; initType < 3; ++initType)
      for (var qp = 0; qp <= 51; ++qp) {
        H265CabacContexts.Initialize(states, initType, qp);

        foreach (var state in states)
          Assert.That(state >> 1, Is.InRange(0, 62),
            $"initType {initType}, quantiser {qp}: the terminating state 63 is never an initial one");
      }
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserIsClippedRatherThanExtrapolated() {
    var low = new byte[H265CabacContexts.COUNT];
    var negative = new byte[H265CabacContexts.COUNT];
    var high = new byte[H265CabacContexts.COUNT];
    var beyond = new byte[H265CabacContexts.COUNT];

    H265CabacContexts.Initialize(low, 0, 0);
    H265CabacContexts.Initialize(negative, 0, -12);
    H265CabacContexts.Initialize(high, 0, 51);
    H265CabacContexts.Initialize(beyond, 0, 70);

    Assert.That(negative, Is.EqualTo(low));
    Assert.That(beyond, Is.EqualTo(high));
  }

  [Test]
  [Category("Unit")]
  public void AKnownInitialisationValueGivesTheStateTheStandardsArithmeticProduces() {
    // sao_merge_left_flag, whose initialisation value is 153 for every slice type, at quantiser 26.
    // Slope (153 >> 4) * 5 - 45 = 0, intercept ((153 & 15) << 3) - 16 = 56, so the state does not
    // depend on the quantiser at all and lands just below the midpoint: the more probable symbol is
    // zero and the estimate is the weakest there is.
    var states = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(states, 0, 26);

    var state = states[H265CabacContexts.SAO_MERGE];
    Assert.That(state & 1, Is.EqualTo(0), "the more probable symbol");
    Assert.That(state >> 1, Is.EqualTo(7), "the probability state");
  }

  [Test]
  [Category("Unit")]
  public void TheContextTableCoversEverySyntaxElementExactlyOnce() {
    // The offsets are declared one after another, each the previous plus its own count, so the last
    // of them plus its count is the total. A syntax element inserted without moving the ones after
    // it would break this — and would otherwise show up only as a stream that decodes to noise.
    Assert.That(H265CabacContexts.COEFF_ABS_LEVEL_GREATER2_FLAG + 6, Is.EqualTo(H265CabacContexts.COUNT));
    Assert.That(H265CabacContexts.SIG_COEFF_FLAG + 42, Is.EqualTo(H265CabacContexts.COEFF_ABS_LEVEL_GREATER1_FLAG));
    Assert.That(H265CabacContexts.LAST_SIG_COEFF_X_PREFIX + 18, Is.EqualTo(H265CabacContexts.LAST_SIG_COEFF_Y_PREFIX));
  }

  // ==============================================================================================
  // The quantiser tables — clause 8.6.1
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void TheChromaQuantiserFollowsLumaBelowThirtyAndTrailsItBySixAboveFortyThree() {
    for (var qp = 0; qp < 30; ++qp)
      Assert.That(H265Dequantiser.ChromaQp(qp), Is.EqualTo(qp));

    for (var qp = 44; qp <= 57; ++qp)
      Assert.That(H265Dequantiser.ChromaQp(qp), Is.EqualTo(qp - 6));

    // In between it climbs at about half luma's rate, never falling and never gaining more than one.
    for (var qp = 30; qp <= 43; ++qp) {
      var step = H265Dequantiser.ChromaQp(qp) - H265Dequantiser.ChromaQp(qp - 1);
      Assert.That(step, Is.InRange(0, 1), $"quantiser {qp}");
    }
  }
}
