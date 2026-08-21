namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The quantisation matrices VP3 builds from its three base matrices and its two scale tables.
/// </summary>
/// <remarks>
/// Section 6.4.3 of the Theora specification builds a matrix by interpolating between the base
/// matrices at either end of a range of quantisation indices. VP3 has one range covering every index
/// with the same base matrix at both ends, so the interpolation has nothing to do and the base matrix
/// is used as it stands — which is worth a test, because collapsing an interpolation is exactly the
/// kind of simplification that is right until it is not.
/// </remarks>
[TestFixture]
public sealed class Vp3QuantisationTests {

  private static int[] _Build(int quantisationType, int plane, int quantisationIndex) {
    var matrix = new int[64];
    Vp3Quantisation.Build(quantisationType, plane, quantisationIndex, matrix);
    return matrix;
  }

  [Test]
  [Category("Unit")]
  public void CollapsingTheInterpolationGivesWhatInterpolatingWouldGive() {
    // With the same base matrix at both ends of a range of size 63, the specification's weighted
    // average is (2*(63-qi)*B + 2*qi*B + 63) / 126, which is B for every qi. This is that identity,
    // computed the long way and compared against the short way the decoder takes.
    for (var quantisationType = 0; quantisationType < 2; ++quantisationType)
    for (var plane = 0; plane < 3; ++plane) {
      var baseMatrix = Vp3Tables.BaseMatrices[Vp3Tables.BaseMatrixOf[quantisationType][plane]];

      for (var quantisationIndex = 0; quantisationIndex < 64; ++quantisationIndex) {
        var matrix = _Build(quantisationType, plane, quantisationIndex);

        for (var coefficient = 0; coefficient < 64; ++coefficient) {
          var interpolated =
            (2 * (63 - quantisationIndex) * baseMatrix[coefficient]
              + 2 * quantisationIndex * baseMatrix[coefficient] + 63) / 126;
          var scale = coefficient == 0
            ? Vp3Tables.DcScale[quantisationIndex]
            : Vp3Tables.AcScale[quantisationIndex];
          var floor = quantisationType == 0 ? coefficient == 0 ? 16 : 8 : coefficient == 0 ? 32 : 16;
          var expected = System.Math.Max(floor, System.Math.Min(scale * interpolated / 100 * 4, 4096));

          Assert.That(matrix[coefficient], Is.EqualTo(expected),
            $"type {quantisationType}, plane {plane}, index {quantisationIndex}, coefficient {coefficient}");
        }
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryQuantiserObeysItsFloorAndItsCeiling() {
    // Table 6.18: sixteen and eight for intra blocks, thirty-two and sixteen for inter, and nothing
    // above 4096 anywhere. The floor is what stops the coarsest end of the range from quantising
    // finely enough to overflow the transform.
    for (var quantisationType = 0; quantisationType < 2; ++quantisationType)
    for (var plane = 0; plane < 3; ++plane)
    for (var quantisationIndex = 0; quantisationIndex < 64; ++quantisationIndex) {
      var matrix = _Build(quantisationType, plane, quantisationIndex);

      for (var coefficient = 0; coefficient < 64; ++coefficient) {
        var floor = quantisationType == 0 ? coefficient == 0 ? 16 : 8 : coefficient == 0 ? 32 : 16;
        Assert.That(matrix[coefficient], Is.InRange(floor, 4096),
          $"type {quantisationType}, plane {plane}, index {quantisationIndex}, coefficient {coefficient}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AHigherQuantisationIndexNeverMeansACoarserQuantiser() {
    // A higher index means finer quantisation, so no quantiser rises as the index does.
    for (var quantisationType = 0; quantisationType < 2; ++quantisationType)
    for (var plane = 0; plane < 3; ++plane) {
      var previous = _Build(quantisationType, plane, 0);

      for (var quantisationIndex = 1; quantisationIndex < 64; ++quantisationIndex) {
        var matrix = _Build(quantisationType, plane, quantisationIndex);

        for (var coefficient = 0; coefficient < 64; ++coefficient)
          Assert.That(matrix[coefficient], Is.LessThanOrEqualTo(previous[coefficient]),
            $"type {quantisationType}, plane {plane}, index {quantisationIndex}, coefficient {coefficient}");

        previous = matrix;
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void BothChromaPlanesShareOneMatrixAndTheLumaPlaneHasItsOwn() {
    // VP3 has one base matrix for the luma plane of an intra block, one for both chroma planes of an
    // intra block, and one for every plane of an inter block.
    Assert.That(_Build(0, 1, 30), Is.EqualTo(_Build(0, 2, 30)));
    Assert.That(_Build(0, 0, 30), Is.Not.EqualTo(_Build(0, 1, 30)));
    Assert.That(_Build(1, 0, 30), Is.EqualTo(_Build(1, 1, 30)));
    Assert.That(_Build(1, 0, 30), Is.EqualTo(_Build(1, 2, 30)));
  }
}
