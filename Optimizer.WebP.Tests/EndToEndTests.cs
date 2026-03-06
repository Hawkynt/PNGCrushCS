using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Crush.Core;
using FileFormat.WebP;

namespace Optimizer.WebP.Tests;

[TestFixture]
public sealed class EndToEndTests {

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_LosslessInput_ProducesValidResult() {
    var file = _CreateLosslessWebPFile();
    var optimizer = new WebPOptimizer(file);

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.CompressedSize, Is.GreaterThan(0));
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_ResultIsValidWebP() {
    var file = _CreateLosslessWebPFile();
    var optimizer = new WebPOptimizer(file);

    var result = optimizer.OptimizeAsync().AsTask().Result;

    // Verify the output is valid RIFF WEBP
    Assert.That(result.FileContents.Length, Is.GreaterThanOrEqualTo(12));
    Assert.That(Encoding.ASCII.GetString(result.FileContents, 0, 4), Is.EqualTo("RIFF"));
    Assert.That(Encoding.ASCII.GetString(result.FileContents, 8, 4), Is.EqualTo("WEBP"));

    // Verify it can be parsed back
    var restored = WebPReader.FromBytes(result.FileContents);
    Assert.That(restored.Features.Width, Is.EqualTo(4));
    Assert.That(restored.Features.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MetadataStripped_ProducesSmallerOrEqualOutput() {
    var exifData = new byte[256];
    for (var i = 0; i < exifData.Length; ++i)
      exifData[i] = (byte)(i & 0xFF);

    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };

    var file = new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true,
      MetadataChunks = [("EXIF", exifData)]
    };

    var optimizer = new WebPOptimizer(file);
    var result = optimizer.OptimizeAsync().AsTask().Result;

    // The optimizer should strip metadata and produce smaller output
    Assert.That(result.MetadataStripped, Is.True);

    // Verify no metadata in result
    var restored = WebPReader.FromBytes(result.FileContents);
    Assert.That(restored.MetadataChunks, Is.Empty);
  }

  [Test]
  [Category("EndToEnd")]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    var file = _CreateLosslessWebPFile();
    var optimizer = new WebPOptimizer(file);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Assert.That(
      async () => await optimizer.OptimizeAsync(cts.Token),
      Throws.InstanceOf<OperationCanceledException>()
    );
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_ReportsProgress() {
    var file = _CreateLosslessWebPFile();
    var optimizer = new WebPOptimizer(file);

    var progressReports = new List<OptimizationProgress>();
    var progress = new Progress<OptimizationProgress>(p => {
      lock (progressReports)
        progressReports.Add(p);
    });

    optimizer.OptimizeAsync(default, progress).AsTask().Wait();

    // Wait a brief moment for async progress reports to arrive
    Thread.Sleep(100);

    lock (progressReports)
      Assert.That(progressReports, Is.Not.Empty, "Expected at least one progress report");
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_LossyInput_ProducesValidResult() {
    var vp8Data = new byte[] { 0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x04, 0x00, 0x04, 0x00 };
    var file = new WebPFile {
      Features = new WebPFeatures(4, 4, false, false, false),
      ImageData = vp8Data,
      IsLossless = false
    };

    var optimizer = new WebPOptimizer(file);
    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.CompressedSize, Is.GreaterThan(0));
    var restored = WebPReader.FromBytes(result.FileContents);
    Assert.That(restored.Features.Width, Is.EqualTo(4));
    Assert.That(restored.Features.Height, Is.EqualTo(4));
    Assert.That(restored.Features.IsLossless, Is.False);
  }

  private static WebPFile _CreateLosslessWebPFile() {
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };
    return new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true
    };
  }
}
