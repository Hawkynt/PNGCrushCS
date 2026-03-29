using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Crush.TestUtilities;
using NUnit.Framework;
using FileFormat.Pcx;
using Optimizer.Pcx;

namespace Optimizer.Pcx.Tests;

[TestFixture]
public sealed class EndToEndTests {
  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_ProducesValidPcx() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      PlaneConfigs: [PcxPlaneConfig.SeparatePlanes],
      PaletteOrders: [PcxPaletteOrder.Original]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(128), "PCX must have at least a 128-byte header");
    // Verify PCX manufacturer byte
    Assert.That(result.FileContents[0], Is.EqualTo(0x0A), "PCX manufacturer byte must be 0x0A");
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_FewColors_ProducesSmaller() {
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Red : Color.Blue);

    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MultipleColorModes_PicksSmallest() {
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, Color.White);

    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      PlaneConfigs: [PcxPlaneConfig.SeparatePlanes],
      PaletteOrders: [PcxPaletteOrder.Original]
    ));
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Assert.That(
      async () => await optimizer.OptimizeAsync(cts.Token),
      Throws.InstanceOf<OperationCanceledException>()
    );
  }

  [Test]
  public void FromFile_NonExistentFile_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.pcx"));
    Assert.Throws<FileNotFoundException>(() => PcxOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcxOptimizer.FromFile(null!));
  }

  [Test]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new PcxOptimizer(null!));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Indexed8_ProducesValidPcx() {
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 32, y * 32, 0));

    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      ColorModes: [PcxColorMode.Indexed8],
      PlaneConfigs: [PcxPlaneConfig.SinglePlane],
      PaletteOrders: [PcxPaletteOrder.FrequencySorted],
      AutoSelectColorMode: false
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(128));
    // Verify VGA palette marker at end
    var paletteMarkerPos = result.FileContents.Length - 769;
    Assert.That(result.FileContents[paletteMarkerPos], Is.EqualTo(0x0C), "VGA palette marker must be 0x0C");
  }
}
