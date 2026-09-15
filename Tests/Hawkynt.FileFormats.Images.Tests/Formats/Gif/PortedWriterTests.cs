using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>The writer coverage the external <c>Hawkynt.GifFileFormat</c> package carried
/// (<c>AnythingToGif.Tests/GifWriterTests.cs</c> and the writer half of its integration and
/// edge-case fixtures), brought across when this codec became the only one. The originals asserted
/// that a file appeared and was non-empty; these assert the same shapes and then read them back,
/// because "non-empty" would not have caught a mis-sized colour table.</summary>
[TestFixture]
public sealed class PortedWriterTests {

  private static readonly byte[] _Palette256 = _BuildPalette();

  private static byte[] _BuildPalette() {
    var p = new byte[768];
    for (var i = 0; i < 256; ++i) {
      p[i * 3] = (byte)i;
      p[i * 3 + 1] = (byte)(255 - i);
      p[i * 3 + 2] = (byte)((i * 37) & 0xFF);
    }
    return p;
  }

  private static Frame _Solid(ushort w, ushort h, byte index, int delayMs,
    FrameDisposalMethod disposal = FrameDisposalMethod.Unspecified, byte? transparent = null) {
    var pixels = new byte[w * h];
    for (var i = 0; i < pixels.Length; ++i) pixels[i] = index;
    return new Frame {
      Left = 0, Top = 0, Width = w, Height = h,
      PixelData = pixels,
      Delay = TimeSpan.FromMilliseconds(delayMs),
      DisposalMethod = disposal,
      TransparentColorIndex = transparent,
    };
  }

  private static GifFile _File(Dimensions size, IReadOnlyList<Frame> frames, LoopCount loop, byte[]? gct = null) => new() {
    Version = GifVersion.Gif89a,
    LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
      Width: size.Width, Height: size.Height,
      HasGlobalColorTable: (gct ?? _Palette256).Length > 0,
      ColorResolution: 8, GlobalColorTableSorted: false,
      GlobalColorTableSize: 0, BackgroundColorIndex: 0, PixelAspectRatio: 0),
    GlobalColorTable = gct ?? _Palette256,
    LoopCount = loop,
    Frames = frames,
  };

  private static string _TempPath() => Path.Combine(Path.GetTempPath(), $"gifported-{Guid.NewGuid():N}.gif");

  // ---- ToFile_CreatesSingleFrameGif_Successfully ----
  [Test]
  public void SingleFrame_IsWrittenAndReadBack() {
    var path = _TempPath();
    try {
      Writer.ToFile(new FileInfo(path), new Dimensions(100, 100),
        [_Solid(100, 100, 1, 100)], LoopCount.Infinite, globalColorTable: _Palette256);

      var file = new FileInfo(path);
      Assert.That(file.Exists, Is.True);
      Assert.That(file.Length, Is.GreaterThan(0));

      var reread = GifReader.FromFile(file);
      Assert.That(reread.Frames, Has.Count.EqualTo(1));
      Assert.That(reread.Frames[0].Width, Is.EqualTo(100));
      Assert.That(reread.Frames[0].Height, Is.EqualTo(100));
      Assert.That(reread.Frames[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
    } finally {
      File.Delete(path);
    }
  }

  // ---- ToFile_CreatesMultiFrameGif_Successfully ----
  [Test]
  public void MultiFrame_KeepsEveryFrameInOrder() {
    var frames = new[] { _Solid(50, 50, 1, 200), _Solid(50, 50, 2, 200), _Solid(50, 50, 3, 200) };
    var reread = GifReader.FromBytes(GifWriter.ToBytes(_File(new Dimensions(50, 50), frames, LoopCount.Infinite)));

    Assert.That(reread.Frames, Has.Count.EqualTo(3));
    for (var i = 0; i < 3; ++i)
      Assert.That(reread.Frames[i].PixelData[0], Is.EqualTo((byte)(i + 1)), $"frame {i}");
  }

  // ---- ToFile_WithGlobalColorTable_CreatesValidGif ----
  [Test]
  public void GlobalColorTable_OfThreeColors_IsWrittenAndReadBack() {
    var gct = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
    var reread = GifReader.FromBytes(GifWriter.ToBytes(
      _File(new Dimensions(25, 25), [_Solid(25, 25, 2, 100)], LoopCount.Infinite, gct)));

    Assert.That(reread.LogicalScreenDescriptor.HasGlobalColorTable, Is.True);
    Assert.That(reread.GlobalColorTable, Is.Not.Null);
    Assert.That(reread.GlobalColorTable![0..9], Is.EqualTo(gct));
    Assert.That(reread.Frames[0].PixelData[0], Is.EqualTo(2));
  }

  // ---- ToFile_WithTransparency_CreatesValidGif ----
  [Test]
  public void Transparency_SurvivesTheRoundTrip() {
    var reread = GifReader.FromBytes(GifWriter.ToBytes(
      _File(new Dimensions(30, 30), [_Solid(30, 30, 1, 100, transparent: 0)], LoopCount.Infinite)));

    Assert.That(reread.Frames[0].TransparentColorIndex, Is.EqualTo((byte)0));
  }

  // ---- ToFile_WithDifferentFrameDisposalMethods_CreatesValidGif ----
  [Test]
  public void EveryDisposalMethod_SurvivesTheRoundTrip() {
    var methods = new[] {
      FrameDisposalMethod.Unspecified,
      FrameDisposalMethod.DoNotDispose,
      FrameDisposalMethod.RestoreToBackground,
      FrameDisposalMethod.RestoreToPrevious,
    };
    var frames = new List<Frame>();
    foreach (var m in methods) frames.Add(_Solid(40, 40, 7, 150, m));

    var reread = GifReader.FromBytes(GifWriter.ToBytes(_File(new Dimensions(40, 40), frames, LoopCount.Infinite)));

    Assert.That(reread.Frames, Has.Count.EqualTo(methods.Length));
    for (var i = 0; i < methods.Length; ++i)
      Assert.That(reread.Frames[i].DisposalMethod, Is.EqualTo(methods[i]), $"frame {i}");
  }

  // ---- ToFile_WithCustomLoopCount_CreatesValidGif ----
  [Test]
  public void CustomLoopCount_SurvivesTheRoundTrip() {
    var reread = GifReader.FromBytes(GifWriter.ToBytes(
      _File(new Dimensions(20, 20), [_Solid(20, 20, 3, 100)], (LoopCount)(ushort)5)));

    Assert.That(reread.LoopCount.IsSet, Is.True);
    Assert.That(reread.LoopCount.Value, Is.EqualTo(5));
  }

  // ---- ToFile_WithCompressionEnabled_CreatesValidGif ----
  [Test]
  public void CompressionOnAndOff_BothProduceReadableFiles() {
    var file = _File(new Dimensions(60, 60), [_Solid(60, 60, 4, 100)], LoopCount.Infinite);

    var compressed = GifWriter.ToBytes(file, GifWriteOptions.Default);
    var stored = GifWriter.ToBytes(file, GifWriteOptions.NoCompression);

    Assert.That(GifReader.FromBytes(compressed).Frames[0].PixelData[0], Is.EqualTo(4));
    Assert.That(GifReader.FromBytes(stored).Frames[0].PixelData[0], Is.EqualTo(4));
    Assert.That(compressed.Length, Is.LessThan(stored.Length), "a solid frame compresses");
  }

  // ---- ToFile_ThrowsArgumentNullException_WhenParametersAreNull ----
  [Test]
  public void NullArguments_Throw() {
    Assert.Throws<ArgumentNullException>(() =>
      Writer.ToFile(null!, new Dimensions(10, 10), Array.Empty<Frame>(), LoopCount.Infinite));
    Assert.Throws<ArgumentNullException>(() =>
      Writer.ToFile(new FileInfo(_TempPath()), new Dimensions(10, 10), null!, LoopCount.Infinite));
    Assert.Throws<ArgumentNullException>(() => GifWriter.ToBytes(null!));
    Assert.Throws<ArgumentNullException>(() => GifWriter.WriteTo(null!, new MemoryStream()));
  }

  // ---- GifWriter_HandlesExtremelySmallImages ----
  [Test]
  public void OnePixelImage_RoundTrips() {
    var reread = GifReader.FromBytes(GifWriter.ToBytes(
      _File(new Dimensions(1, 1), [_Solid(1, 1, 1, 100)], LoopCount.Infinite)));

    Assert.That(reread.Frames, Has.Count.EqualTo(1));
    Assert.That(reread.Frames[0].Width, Is.EqualTo(1));
    Assert.That(reread.Frames[0].Height, Is.EqualTo(1));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(new byte[] { 1 }));
  }

  // ---- GifWriter_HandlesLargeImages ----
  [Test]
  public void LargeImage_RoundTrips() {
    var reread = GifReader.FromBytes(GifWriter.ToBytes(
      _File(new Dimensions(500, 500), [_Solid(500, 500, 9, 100)], LoopCount.Infinite)));

    Assert.That(reread.Frames[0].PixelData, Has.Length.EqualTo(500 * 500));
    Assert.That(reread.Frames[0].PixelData[250 * 500 + 250], Is.EqualTo(9));
  }

  // ---- GifWriter_HandlesManyFrames ----
  [Test]
  public void HundredFrames_AllSurvive() {
    var frames = new List<Frame>();
    for (var i = 0; i < 100; ++i) frames.Add(_Solid(10, 10, (byte)i, 50));

    var reread = GifReader.FromBytes(GifWriter.ToBytes(_File(new Dimensions(10, 10), frames, LoopCount.Infinite)));

    Assert.That(reread.Frames, Has.Count.EqualTo(100));
    for (var i = 0; i < 100; ++i)
      Assert.That(reread.Frames[i].PixelData[0], Is.EqualTo((byte)i), $"frame {i}");
  }

  // ---- GifWriter_HandlesVeryShortFrameDurations ----
  [Test]
  public void OneMillisecondDelay_RoundsToZeroHundredths_AndStillWritesAGce() {
    // GIF delays are in hundredths of a second, so 1 ms cannot be represented; it becomes 0.
    // The frame still needs its GCE because it carries a disposal method.
    var frames = new[] {
      _Solid(20, 20, 1, 1, FrameDisposalMethod.DoNotDispose),
      _Solid(20, 20, 2, 1, FrameDisposalMethod.DoNotDispose),
    };
    var reread = GifReader.FromBytes(GifWriter.ToBytes(_File(new Dimensions(20, 20), frames, LoopCount.Infinite)));

    Assert.That(reread.Frames, Has.Count.EqualTo(2));
    Assert.That(reread.Frames[0].Delay, Is.EqualTo(TimeSpan.Zero));
    Assert.That(reread.Frames[0].DisposalMethod, Is.EqualTo(FrameDisposalMethod.DoNotDispose));
  }

  [Test]
  public void SubHundredthDelays_RoundToTheNearestHundredth() {
    // The external writer truncated (14 ms became 10 ms, 16 ms became 10 ms); this one rounds to
    // nearest, which keeps a frame sequence's total duration closer to what was asked for.
    foreach (var (ms, expected) in new[] { (14, 10), (16, 20), (24, 20), (26, 30) }) {
      var reread = GifReader.FromBytes(GifWriter.ToBytes(
        _File(new Dimensions(4, 4), [_Solid(4, 4, 1, ms)], LoopCount.Infinite)));
      Assert.That(reread.Frames[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(expected)), $"{ms} ms");
    }
  }

  // ---- FileSystem_HandlesSpecialCharactersInFilenames ----
  [Test]
  public void FilenameWithSpacesAndSymbols_IsWritten() {
    var dir = Path.Combine(Path.GetTempPath(), $"gifported-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "test with spaces & symbols #@$.gif");
    try {
      Writer.ToFile(new FileInfo(path), new Dimensions(10, 10),
        [_Solid(10, 10, 1, 100)], LoopCount.Infinite, globalColorTable: _Palette256);

      Assert.That(File.Exists(path), Is.True);
      Assert.That(GifReader.FromFile(new FileInfo(path)).Frames, Has.Count.EqualTo(1));
    } finally {
      Directory.Delete(dir, true);
    }
  }

  // ---- ToFile overwrites rather than appending ----
  [Test]
  public void ToFile_OverwritesAnExistingFile() {
    var path = _TempPath();
    try {
      File.WriteAllBytes(path, new byte[4096]);
      Writer.ToFile(new FileInfo(path), new Dimensions(4, 4),
        [_Solid(4, 4, 1, 100)], LoopCount.Infinite, globalColorTable: _Palette256);

      Assert.That(new FileInfo(path).Length, Is.LessThan(4096));
      Assert.That(GifReader.FromFile(new FileInfo(path)).Frames, Has.Count.EqualTo(1));
    } finally {
      File.Delete(path);
    }
  }
}
