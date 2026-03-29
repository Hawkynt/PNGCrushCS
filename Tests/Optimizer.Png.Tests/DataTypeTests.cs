using System.Runtime.Versioning;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class DataTypeTests {
  [Test]
  public void ColorMode_ValuesMatchPngSpec() {
    Assert.That((byte)PngColorType.Grayscale, Is.EqualTo(0));
    Assert.That((byte)PngColorType.RGB, Is.EqualTo(2));
    Assert.That((byte)PngColorType.Palette, Is.EqualTo(3));
    Assert.That((byte)PngColorType.GrayscaleAlpha, Is.EqualTo(4));
    Assert.That((byte)PngColorType.RGBA, Is.EqualTo(6));
  }

  [Test]
  public void InterlaceMethod_ValuesMatchPngSpec() {
    Assert.That((byte)PngInterlaceMethod.None, Is.EqualTo(0));
    Assert.That((byte)PngInterlaceMethod.Adam7, Is.EqualTo(1));
  }

  [Test]
  public void FilterType_ValuesMatchPngSpec() {
    Assert.That((int)PngFilterType.None, Is.EqualTo(0));
    Assert.That((int)PngFilterType.Sub, Is.EqualTo(1));
    Assert.That((int)PngFilterType.Up, Is.EqualTo(2));
    Assert.That((int)PngFilterType.Average, Is.EqualTo(3));
    Assert.That((int)PngFilterType.Paeth, Is.EqualTo(4));
  }

  [Test]
  public void FilterType_HasExactlyFiveValues() {
    var values = Enum.GetValues<PngFilterType>();
    Assert.That(values, Has.Length.EqualTo(5));
  }

  [Test]
  public void DeflateMethod_HasExpectedValues() {
    Assert.That((int)DeflateMethod.Fastest, Is.EqualTo(0));
    Assert.That((int)DeflateMethod.Fast, Is.EqualTo(1));
    Assert.That((int)DeflateMethod.Default, Is.EqualTo(2));
    Assert.That((int)DeflateMethod.Maximum, Is.EqualTo(3));
    Assert.That((int)DeflateMethod.Ultra, Is.EqualTo(4));
    Assert.That((int)DeflateMethod.Hyper, Is.EqualTo(5));
  }

  [Test]
  public void FilterStrategy_HasExpectedValues() {
    var values = Enum.GetValues<FilterStrategy>();
    Assert.That(values, Has.Length.EqualTo(6));
    Assert.That(values, Does.Contain(FilterStrategy.SingleFilter));
    Assert.That(values, Does.Contain(FilterStrategy.ScanlineAdaptive));
    Assert.That(values, Does.Contain(FilterStrategy.WeightedContinuity));
    Assert.That(values, Does.Contain(FilterStrategy.PartitionOptimized));
    Assert.That(values, Does.Contain(FilterStrategy.BruteForce));
    Assert.That(values, Does.Contain(FilterStrategy.BruteForceAdaptive));
  }

  [Test]
  public void OptimizationCombo_IsReadonlyRecordStruct() {
    var combo = new OptimizationCombo(PngColorType.RGB, 8, PngInterlaceMethod.None, FilterStrategy.SingleFilter,
      DeflateMethod.Default);
    Assert.That(combo.ColorMode, Is.EqualTo(PngColorType.RGB));
    Assert.That(combo.BitDepth, Is.EqualTo(8));
    Assert.That(combo.InterlaceMethod, Is.EqualTo(PngInterlaceMethod.None));
    Assert.That(combo.FilterStrategy, Is.EqualTo(FilterStrategy.SingleFilter));
    Assert.That(combo.DeflateMethod, Is.EqualTo(DeflateMethod.Default));
  }

  [Test]
  public void ImageStats_RecordEquality() {
    var stats1 = new ImageStats(10, 12, true, false);
    var stats2 = new ImageStats(10, 12, true, false);
    Assert.That(stats1, Is.EqualTo(stats2));
  }

  [Test]
  public void SmartPartitioningParams_DefaultValues() {
    var p = SmartPartitioningParams.Default;
    Assert.That(p.MinRowsForMinorImprovement, Is.EqualTo(5));
    Assert.That(p.MinRowsForStrongImprovement, Is.EqualTo(2));
    Assert.That(p.MinorImprovementThreshold, Is.EqualTo(1.1).Within(0.001));
    Assert.That(p.StrongImprovementThreshold, Is.EqualTo(1.3).Within(0.001));
  }

  [Test]
  public void PngOptimizationOptions_DefaultValues() {
    var opts = new PngOptimizationOptions();
    Assert.That(opts.AutoSelectColorMode, Is.True);
    Assert.That(opts.TryInterlacing, Is.True);
    Assert.That(opts.TryPartitioning, Is.True);
    Assert.That(opts.MaxPaletteColors, Is.EqualTo(256));
    Assert.That(opts.FilterStrategies, Has.Count.EqualTo(4));
    Assert.That(opts.DeflateMethods, Has.Count.EqualTo(2));
    Assert.That(opts.ZopfliIterations, Is.EqualTo(15));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new PngOptimizer(null!));
  }
}
