using FileFormat.Bmp;
using FileFormat.Core;
using NUnit.Framework;
using Optimizer.Bmp;

namespace Optimizer.Bmp.Tests;

[TestFixture]
public sealed class AlphaPreservationTests {
  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async System.Threading.Tasks.Task OptimizeAsync_TransparentImage_PreservesEveryChannel() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 255,    0, 255, 0, 192,    0, 0, 255, 128,
        10, 20, 30, 64,    40, 50, 60, 1,      70, 80, 90, 0,
      ]
    };

    var optimizer = new BmpOptimizer(source, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp, BmpRowOrder.TopDown]
    ));

    var result = await optimizer.OptimizeAsync();
    var decoded = BmpFile.ToRawImage(BmpReader.FromBytes(result.FileContents)).EnsureFormat(PixelFormat.Rgba32);

    Assert.Multiple(() => {
      Assert.That(result.ColorMode, Is.EqualTo(BmpColorMode.Bgra32));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_TransparentImage_ExplicitOpaqueOnlyMode_RefusesDataLoss() {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [1, 2, 3, 4]
    };

    var optimizer = new BmpOptimizer(source, new BmpOptimizationOptions(
      ColorModes: [BmpColorMode.Rgb24],
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp],
      AutoSelectColorMode: false
    ));

    var exception = Assert.ThrowsAsync<System.InvalidOperationException>(async () => await optimizer.OptimizeAsync());
    Assert.That(exception!.Message, Does.Contain("No valid optimization result"));
  }
}
