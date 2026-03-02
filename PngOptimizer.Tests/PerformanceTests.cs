using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;

namespace PngOptimizer.Tests;

[TestFixture]
[Category("Performance")]
[SupportedOSPlatform("windows")]
public sealed class PerformanceTests {
  [Test]
  [Timeout(60000)]
  public async Task StressTestPng_CompletesWithinTimeout() {
    var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "StressTest.png");
    if (!File.Exists(fixturePath))
      Assert.Ignore("StressTest.png fixture not found");

    using var bmp = new Bitmap(fixturePath);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Fastest],
      MaxParallelTasks = Environment.ProcessorCount
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  public void FilterApplication_Throughput() {
    const int scanlineLength = 4096;
    const int rowCount = 100;

    var imageData = new byte[rowCount][];
    for (var y = 0; y < rowCount; ++y) {
      imageData[y] = new byte[scanlineLength];
      for (var x = 0; x < scanlineLength; ++x)
        imageData[y][x] = (byte)((y * scanlineLength + x) % 256);
    }

    var filters = new FilterType[rowCount];
    for (var y = 0; y < rowCount; ++y)
      filters[y] = (FilterType)(y % 5);

    var sw = Stopwatch.StartNew();
    var result = FilterTools.ApplyFilters(imageData, filters, 3);
    sw.Stop();

    Assert.That(result, Has.Length.EqualTo(rowCount));
    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));
  }

  [Test]
  public void FilterSelector_Throughput() {
    const int scanlineLength = 1024;
    const int rowCount = 50;

    var imageData = new byte[rowCount][];
    for (var y = 0; y < rowCount; ++y) {
      imageData[y] = new byte[scanlineLength];
      Random.Shared.NextBytes(imageData[y]);
    }

    var optimizer = new PngFilterOptimizer(scanlineLength, rowCount, 3, false, false, 8, imageData);

    var sw = Stopwatch.StartNew();
    var filters = optimizer.OptimizeFilters(FilterStrategy.ScanlineAdaptive);
    sw.Stop();

    Assert.That(filters, Has.Length.EqualTo(rowCount));
    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));
  }
}
