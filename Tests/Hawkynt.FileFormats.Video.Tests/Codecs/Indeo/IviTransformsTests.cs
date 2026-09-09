using System.Linq;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The inverse transforms, and the DC shortcuts that have to agree with them.
/// </summary>
/// <remarks>
/// An uncoded intra block does not go through the transform at all: it is filled from the running DC
/// value by a separate routine, and the format only works because that routine produces what the full
/// transform would have produced for a block whose one coefficient is that DC value. Checking the two
/// against each other is the one property of these transforms that can be tested without a bitstream,
/// and it catches a rounding term in the wrong place, which is the mistake that costs one sample
/// value everywhere and grows through the prediction loop.
/// </remarks>
[TestFixture]
public sealed class IviTransformsTests {

  private const int _PITCH = 16;

  private static (int[] Coefficients, byte[] Flags) _DcOnly(int dc) {
    var flags = new byte[8];
    flags[0] = 1;
    var coefficients = new int[64];
    coefficients[0] = dc;
    return (coefficients, flags);
  }

  [TestCase(2)]
  [TestCase(8)]
  [TestCase(-16)]
  [TestCase(254)]
  [Category("Unit")]
  public void TheSlantShortcutAgreesWithTheSlantTransform(int dc) {
    var (coefficients, flags) = _DcOnly(dc);

    var transformed = new short[_PITCH * 8];
    IviTransforms.InverseSlant8x8(coefficients, transformed, 0, _PITCH, flags);

    var shortcut = new short[_PITCH * 8];
    IviTransforms.DcSlant2D(dc, shortcut, 0, _PITCH, 8);

    Assert.That(transformed, Is.EqualTo(shortcut).AsCollection);
  }

  [TestCase(8)]
  [TestCase(-24)]
  [TestCase(64)]
  [Category("Unit")]
  public void TheHaarShortcutAgreesWithTheHaarTransform(int dc) {
    var (coefficients, flags) = _DcOnly(dc);

    var transformed = new short[_PITCH * 8];
    IviTransforms.InverseHaar8x8(coefficients, transformed, 0, _PITCH, flags);

    var shortcut = new short[_PITCH * 8];
    IviTransforms.DcHaar2D(dc, shortcut, 0, _PITCH, 8);

    Assert.That(transformed, Is.EqualTo(shortcut).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void TheRowShortcutFillsOneRowAndLeavesTheRestAtZero() {
    // A one-dimensional transform of a lone DC coefficient is one row, not a flat block: the pass it
    // does not run leaves the other rows where it found them.
    var destination = new short[_PITCH * 8];
    IviTransforms.DcRowSlant(9, destination, 0, _PITCH, 8);

    Assert.That(destination[..8], Is.EqualTo(Enumerable.Repeat((short)5, 8)).AsCollection);
    Assert.That(destination.Skip(_PITCH).Take(8), Is.All.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheColumnShortcutFillsOneColumnAndLeavesTheRestAtZero() {
    var destination = new short[_PITCH * 8];
    IviTransforms.DcColumnSlant(9, destination, 0, _PITCH, 8);

    for (var row = 0; row < 8; ++row) {
      Assert.That(destination[row * _PITCH], Is.EqualTo(5));
      Assert.That(destination.Skip(row * _PITCH + 1).Take(7), Is.All.Zero);
    }
  }

  [Test]
  [Category("Unit")]
  public void AColumnWithNoCoefficientsIsNotTransformed() {
    // The column flags are part of the transform's definition and not an optimisation: a column with
    // no non-zero coefficient is written as zeroes without being transformed at all.
    var coefficients = new int[64];
    for (var i = 0; i < 64; ++i)
      coefficients[i] = i + 1;

    var destination = new short[_PITCH * 8];
    IviTransforms.InverseSlant8x8(coefficients, destination, 0, _PITCH, new byte[8]);

    Assert.That(destination, Is.All.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheUntransformedBandStoresItsCoefficientsAsSamples() {
    var coefficients = new int[64];
    for (var i = 0; i < 64; ++i)
      coefficients[i] = i - 32;

    var destination = new short[_PITCH * 8];
    IviTransforms.PutPixels8x8(coefficients, destination, 0, _PITCH, new byte[8]);

    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        Assert.That(destination[row * _PITCH + column], Is.EqualTo(row * 8 + column - 32));
  }

  [Test]
  [Category("Unit")]
  public void TheUntransformedBandsDcShortcutStoresOneSample() {
    var destination = new short[_PITCH * 8];
    IviTransforms.PutDcPixel8x8(77, destination, 0, _PITCH, 8);

    Assert.That(destination[0], Is.EqualTo(77));
    Assert.That(destination.Skip(1).Take(7), Is.All.Zero);
    Assert.That(destination.Skip(_PITCH).Take(8), Is.All.Zero);
  }

  [TestCase(4)]
  [TestCase(-12)]
  [Category("Unit")]
  public void TheFourByFourTransformsAgreeWithTheirShortcutsToo(int dc) {
    var flags = new byte[8];
    flags[0] = 1;
    var coefficients = new int[64];
    coefficients[0] = dc;

    var slant = new short[_PITCH * 4];
    IviTransforms.InverseSlant4x4(coefficients, slant, 0, _PITCH, flags);
    var slantShortcut = new short[_PITCH * 4];
    IviTransforms.DcSlant2D(dc, slantShortcut, 0, _PITCH, 4);
    Assert.That(slant, Is.EqualTo(slantShortcut).AsCollection);

    var haar = new short[_PITCH * 4];
    IviTransforms.InverseHaar4x4(coefficients, haar, 0, _PITCH, flags);
    var haarShortcut = new short[_PITCH * 4];
    IviTransforms.DcHaar2D(dc, haarShortcut, 0, _PITCH, 4);
    Assert.That(haar, Is.EqualTo(haarShortcut).AsCollection);
  }
}
