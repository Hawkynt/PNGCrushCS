using System;
using System.IO;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>The writer's compression choices. Every level has to produce a stream the reader takes
/// back unchanged — the levels trade size for encode time, never fidelity.</summary>
[TestFixture]
public sealed class WriteOptionsTests {

  private static GifFile _Build(byte[] pixels, ushort w, ushort h) {
    var gct = new byte[768];
    for (var i = 0; i < 256; ++i) { gct[i * 3] = (byte)i; gct[i * 3 + 1] = (byte)(255 - i); gct[i * 3 + 2] = (byte)(i * 7); }
    return new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: w, Height: h, HasGlobalColorTable: true, ColorResolution: 8,
        GlobalColorTableSorted: false, GlobalColorTableSize: 7, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = gct,
      LoopCount = LoopCount.PlayOnce,
      Frames = [new Frame { Left = 0, Top = 0, Width = w, Height = h, PixelData = pixels }],
    };
  }

  private static byte[] _Gradient(int w, int h) {
    var p = new byte[w * h];
    for (var i = 0; i < p.Length; ++i) p[i] = (byte)(i % 256);
    return p;
  }

  [Test]
  public void EveryLevel_RoundTripsIdentically() {
    var pixels = _Gradient(96, 96);
    var file = _Build(pixels, 96, 96);

    foreach (var options in new[] {
      GifWriteOptions.Default,
      GifWriteOptions.NoCompression,
      GifWriteOptions.SmallestOutput,
      new GifWriteOptions { Compression = GifCompressionLevel.Standard, DeferClear = true },
    }) {
      var reread = GifReader.FromBytes(GifWriter.ToBytes(file, options));
      Assert.That(reread.Frames[0].PixelData, Is.EqualTo(pixels), $"{options.Compression}/defer={options.DeferClear}");
    }
  }

  [Test]
  public void NoCompression_IsLargerThanStandard_OnCompressibleInput() {
    var pixels = new byte[64 * 64]; // all one index
    var file = _Build(pixels, 64, 64);

    var stored = GifWriter.ToBytes(file, GifWriteOptions.NoCompression);
    var standard = GifWriter.ToBytes(file, GifWriteOptions.Default);

    Assert.That(stored.Length, Is.GreaterThan(standard.Length));
  }

  [Test]
  public void SmallestOutput_IsNeverLargerThanStandard() {
    var pixels = _Gradient(128, 128);
    var file = _Build(pixels, 128, 128);

    var best = GifWriter.ToBytes(file, GifWriteOptions.SmallestOutput);
    var standard = GifWriter.ToBytes(file, GifWriteOptions.Default);

    Assert.That(best.Length, Is.LessThanOrEqualTo(standard.Length));
  }

  [Test]
  public void NullOptions_MeansDefault() {
    var file = _Build(_Gradient(32, 32), 32, 32);
    Assert.That(GifWriter.ToBytes(file, null), Is.EqualTo(GifWriter.ToBytes(file, GifWriteOptions.Default)));
    Assert.That(GifWriter.ToBytes(file), Is.EqualTo(GifWriter.ToBytes(file, GifWriteOptions.Default)));
  }

  [Test]
  public void Options_ReachTheStreamingWriterToo() {
    var pixels = new byte[48 * 48];
    var lsd = new GifLogicalScreenDescriptor(48, 48, true, 8, false, 7, 0, 0);
    var gct = new byte[768];

    byte[] Emit(GifWriteOptions o) {
      using var ms = new MemoryStream();
      using (var w = new GifStreamWriter(ms, lsd, gct, LoopCount.PlayOnce, GifVersion.Gif89a, o))
        w.WriteFrame(new Frame { Left = 0, Top = 0, Width = 48, Height = 48, PixelData = pixels });
      return ms.ToArray();
    }

    Assert.That(Emit(GifWriteOptions.NoCompression).Length, Is.GreaterThan(Emit(GifWriteOptions.Default).Length));
  }

  [Test]
  public void Factories_CarryTheExpectedSettings() {
    Assert.That(GifWriteOptions.Default.Compression, Is.EqualTo(GifCompressionLevel.Standard));
    Assert.That(GifWriteOptions.Default.DeferClear, Is.False);
    Assert.That(GifWriteOptions.NoCompression.Compression, Is.EqualTo(GifCompressionLevel.None));
    Assert.That(GifWriteOptions.SmallestOutput.Compression, Is.EqualTo(GifCompressionLevel.Best));
  }
}
