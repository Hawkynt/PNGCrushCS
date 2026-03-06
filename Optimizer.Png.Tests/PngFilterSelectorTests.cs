using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class PngFilterSelectorTests {
  [Test]
  public void CalculateSumOfAbsoluteDifferences_KnownInput() {
    byte[] data = [10, 20, 15, 25];
    var result = PngFilterSelector.CalculateSumOfAbsoluteDifferences(data);
    Assert.That(result, Is.EqualTo(10 + 5 + 10));
  }

  [Test]
  public void CalculateSumOfAbsoluteDifferences_AllSameValues_ReturnsZero() {
    byte[] data = [42, 42, 42, 42];
    var result = PngFilterSelector.CalculateSumOfAbsoluteDifferences(data);
    Assert.That(result, Is.EqualTo(0));
  }

  [Test]
  public void CalculateSumOfAbsoluteDifferences_SingleElement_ReturnsZero() {
    byte[] data = [100];
    var result = PngFilterSelector.CalculateSumOfAbsoluteDifferences(data);
    Assert.That(result, Is.EqualTo(0));
  }

  [Test]
  public void CalculateSumOfAbsoluteDifferences_TwoElements() {
    byte[] data = [10, 50];
    var result = PngFilterSelector.CalculateSumOfAbsoluteDifferences(data);
    Assert.That(result, Is.EqualTo(40));
  }

  [Test]
  public void SelectFilterForScanline_SubBytePalette_ReturnsNone() {
    var selector = new PngFilterSelector(1, false, true, 4);
    byte[] scanline = [0x12, 0x34, 0x56];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(filter, Is.EqualTo(PngFilterType.None));
  }

  [Test]
  public void SelectFilterForScanline_8BitPalette_ReturnsDynamicFilter() {
    var selector = new PngFilterSelector(1, false, true, 8);
    byte[] scanline = [10, 20, 30, 40, 50];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(Enum.IsDefined(filter), Is.True);
  }

  [Test]
  public void SelectFilterForScanline_LowBitGrayscale_ReturnsNone() {
    var selector = new PngFilterSelector(1, true, false, 4);
    byte[] scanline = [0x12, 0x34, 0x56];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(filter, Is.EqualTo(PngFilterType.None));
  }

  [Test]
  public void SelectFilterForScanline_8BitGrayscale_ReturnsDynamicFilter() {
    var selector = new PngFilterSelector(1, true, false, 8);
    byte[] scanline = [10, 20, 30, 40, 50];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(Enum.IsDefined(filter), Is.True);
  }

  [Test]
  public void SelectFilterForScanline_RGB_ReturnsDynamicFilter() {
    var selector = new PngFilterSelector(3, false, false, 8);
    byte[] scanline = [10, 20, 30, 40, 50, 60];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(Enum.IsDefined(filter), Is.True);
  }

  [Test]
  public void SelectFilterForScanline_ConstantRow_PrefersNoneOrSub() {
    var selector = new PngFilterSelector(1, false, false, 8);
    byte[] scanline = [42, 42, 42, 42, 42, 42, 42, 42];
    var filter = selector.SelectFilterForScanline(scanline, ReadOnlySpan<byte>.Empty);
    Assert.That(filter, Is.AnyOf(PngFilterType.None, PngFilterType.Sub));
  }

  [Test]
  public void DeflateAwareScore_ZeroRuns_ScoreLowerThanAlternating() {
    byte[] withZeroRuns = [0, 0, 0, 0, 100, 0, 0, 0, 0];
    byte[] alternating = [50, 51, 50, 51, 50, 51, 50, 51, 50];

    var zeroRunScore = PngFilterSelector.CalculateDeflateAwareScore(withZeroRuns);
    var alternatingScore = PngFilterSelector.CalculateDeflateAwareScore(alternating);

    Assert.That(zeroRunScore, Is.LessThan(alternatingScore));
  }

  [Test]
  public void DeflateAwareScore_AllZeros_NegativeScore() {
    byte[] allZeros = [0, 0, 0, 0, 0, 0, 0, 0];
    var score = PngFilterSelector.CalculateDeflateAwareScore(allZeros);
    Assert.That(score, Is.LessThan(0));
  }

  [Test]
  public void DeflateAwareScore_CircularValues_HighValueTreatedAsSmall() {
    byte[] highValues = [255];
    byte[] lowValues = [1];

    var highScore = PngFilterSelector.CalculateDeflateAwareScore(highValues);
    var lowScore = PngFilterSelector.CalculateDeflateAwareScore(lowValues);

    Assert.That(highScore, Is.EqualTo(lowScore));
  }

  [Test]
  public void DeflateAwareScore_Empty_ReturnsZero() {
    byte[] empty = [];
    var score = PngFilterSelector.CalculateDeflateAwareScore(empty);
    Assert.That(score, Is.EqualTo(0));
  }

  [Test]
  public void DeflateAwareScore_RepeatedNonZeroRuns_LowerThanRandom() {
    byte[] repeated = [42, 42, 42, 42, 100, 100, 100, 100];
    byte[] random = [50, 51, 52, 53, 54, 55, 56, 57];

    var repeatedScore = PngFilterSelector.CalculateDeflateAwareScore(repeated);
    var randomScore = PngFilterSelector.CalculateDeflateAwareScore(random);

    Assert.That(repeatedScore, Is.LessThan(randomScore));
  }

  [Test]
  public void DeflateAwareScore_ZeroRuns_BetterThanNonZeroRuns() {
    byte[] zeroRuns = [0, 0, 0, 0, 0, 0, 0, 0];
    byte[] nonZeroRuns = [42, 42, 42, 42, 42, 42, 42, 42];

    var zeroScore = PngFilterSelector.CalculateDeflateAwareScore(zeroRuns);
    var nonZeroScore = PngFilterSelector.CalculateDeflateAwareScore(nonZeroRuns);

    Assert.That(zeroScore, Is.LessThan(nonZeroScore));
  }

  [Test]
  public void SelectFilter_PerfectZeroRow_BreaksEarly() {
    // All-zero row should trigger early break and return correct filter
    var selector = new PngFilterSelector(1, false, false, 8);
    var scanline = new byte[64]; // all zeros
    var prev = new byte[64]; // all zeros

    var result = selector.SelectFilterForScanline(scanline, prev);
    Assert.That(Enum.IsDefined(result), Is.True);
  }

  [Test]
  public void Stickiness_ReusesFilterForConsistentContent() {
    var selector = new PngFilterSelector(1, false, false, 8);

    // Create 8 identical rows — the filter should stick after a few rows
    var scanline = new byte[64];
    for (var i = 0; i < 64; ++i)
      scanline[i] = (byte)(i * 2);

    var prev = new byte[64];
    for (var i = 0; i < 64; ++i)
      prev[i] = (byte)(i * 2);

    var filters = new PngFilterType[8];
    for (var i = 0; i < 8; ++i)
      filters[i] = selector.SelectFilterForScanline(scanline, prev);

    // All filters should be valid
    foreach (var f in filters)
      Assert.That(Enum.IsDefined(f), Is.True);

    // Should be consistent (all same filter for identical rows)
    for (var i = 1; i < filters.Length; ++i)
      Assert.That(filters[i], Is.EqualTo(filters[0]));
  }

  [Test]
  public void DeflateAwareScore_BytePairRepetition_BetterThanAlternating() {
    // Paired bytes (e.g., AABB) should score lower (better) than alternating (ABAB)
    var paired = new byte[32];
    var alternating = new byte[32];
    for (var i = 0; i < 32; ++i) {
      paired[i] = (byte)(i / 2 % 4);
      alternating[i] = (byte)(i % 4);
    }

    var pairedScore = PngFilterSelector.CalculateDeflateAwareScore(paired);
    var alternatingScore = PngFilterSelector.CalculateDeflateAwareScore(alternating);
    Assert.That(pairedScore, Is.LessThan(alternatingScore));
  }

  [Test]
  public void DeflateAwareScore_HighDiversity_WorseThanLowDiversity() {
    // 16 bytes of all different values should score worse than 16 bytes of low diversity
    var highDiversity = new byte[16];
    for (var i = 0; i < 16; ++i)
      highDiversity[i] = (byte)(i * 17); // 0,17,34,...,255 — 16 unique values

    var lowDiversity = new byte[16];
    for (var i = 0; i < 16; ++i)
      lowDiversity[i] = (byte)(i % 3); // 0,1,2,0,1,2,... — 3 unique values

    var highScore = PngFilterSelector.CalculateDeflateAwareScore(highDiversity);
    var lowScore = PngFilterSelector.CalculateDeflateAwareScore(lowDiversity);
    Assert.That(highScore, Is.GreaterThan(lowScore));
  }
}
