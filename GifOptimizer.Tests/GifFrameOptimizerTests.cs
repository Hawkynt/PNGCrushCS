using System;
using System.Drawing;
using Hawkynt.GifFileFormat;
using NUnit.Framework;

namespace GifOptimizer.Tests;

[TestFixture]
public sealed class GifFrameOptimizerTests {
  [Test]
  [Category("Unit")]
  public void TrimTransparentMargins_NoTransparency_ReturnsOriginal() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var size = new Dimensions(2, 2);
    var position = new Offset(0, 0);

    var (resultPixels, resultPos, resultSize) =
      GifFrameOptimizer.TrimTransparentMargins(pixels, size, position, null);

    Assert.That(resultPixels, Is.SameAs(pixels));
    Assert.That(resultSize, Is.EqualTo(size));
  }

  [Test]
  [Category("Unit")]
  public void TrimTransparentMargins_WithMargins_TrimsCorrectly() {
    // 4x4 frame, transparent index 0, opaque pixel at (1,1)
    var pixels = new byte[] {
      0, 0, 0, 0,
      0, 1, 0, 0,
      0, 0, 0, 0,
      0, 0, 0, 0
    };
    var size = new Dimensions(4, 4);
    var position = new Offset(10, 20);

    var (resultPixels, resultPos, resultSize) = GifFrameOptimizer.TrimTransparentMargins(pixels, size, position, 0);

    Assert.That(resultSize.Width, Is.EqualTo(1));
    Assert.That(resultSize.Height, Is.EqualTo(1));
    Assert.That(resultPos.X, Is.EqualTo(11)); // 10 + 1
    Assert.That(resultPos.Y, Is.EqualTo(21)); // 20 + 1
    Assert.That(resultPixels[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TrimTransparentMargins_AllTransparent_Returns1x1() {
    var pixels = new byte[] { 0, 0, 0, 0 };
    var size = new Dimensions(2, 2);
    var position = new Offset(0, 0);

    var (resultPixels, resultPos, resultSize) = GifFrameOptimizer.TrimTransparentMargins(pixels, size, position, 0);

    Assert.That(resultSize.Width, Is.EqualTo(1));
    Assert.That(resultSize.Height, Is.EqualTo(1));
    Assert.That(resultPixels[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void TrimTransparentMargins_NoMargins_ReturnsOriginal() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var size = new Dimensions(2, 2);
    var position = new Offset(0, 0);

    var (resultPixels, resultPos, resultSize) = GifFrameOptimizer.TrimTransparentMargins(pixels, size, position, 0);

    Assert.That(resultSize, Is.EqualTo(size));
    Assert.That(resultPixels, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void OptimizeDisposalMethods_SingleFrame_ReturnsUnspecified() {
    var gif = _CreateTestGif(1);
    var disposals = GifFrameOptimizer.OptimizeDisposalMethods(gif);

    Assert.That(disposals.Length, Is.EqualTo(1));
    Assert.That(disposals[0], Is.EqualTo(FrameDisposalMethod.Unspecified));
  }

  [Test]
  [Category("Unit")]
  public void OptimizeDisposalMethods_FullCanvasFrames_UsesDoNotDispose() {
    var gif = _CreateTestGif(3);
    var disposals = GifFrameOptimizer.OptimizeDisposalMethods(gif);

    Assert.That(disposals.Length, Is.EqualTo(3));
    Assert.That(disposals[0], Is.EqualTo(FrameDisposalMethod.DoNotDispose));
    Assert.That(disposals[1], Is.EqualTo(FrameDisposalMethod.DoNotDispose));
    Assert.That(disposals[2], Is.EqualTo(FrameDisposalMethod.Unspecified));
  }

  [Test]
  [Category("Unit")]
  public void TryBuildGlobalColorTable_CompatiblePalettes_ReturnsMerged() {
    var gif = _CreateTestGif(2);
    var gct = GifFrameOptimizer.TryBuildGlobalColorTable(gif);

    Assert.That(gct, Is.Not.Null);
    Assert.That(gct!.Length, Is.LessThanOrEqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void OptimizeDisposalMethods_EmptyFrames_ReturnsEmpty() {
    var gif = new GifFile("89a", new Dimensions(4, 4), null, LoopCount.NotSet, 0, []);
    var disposals = GifFrameOptimizer.OptimizeDisposalMethods(gif);
    Assert.That(disposals.Length, Is.EqualTo(0));
  }

  // --- Frame Deduplication Tests ---

  [Test]
  [Category("Unit")]
  public void DeduplicateFrames_IdenticalConsecutive_MergesDelay() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var size = new Dimensions(2, 2);
    var palette = new[] { Color.Red, Color.Green, Color.Blue, Color.White };
    var delay = TimeSpan.FromMilliseconds(100);

    var frames = new[] {
      new Frame(pixels, size, new Offset(0, 0), palette, delay, FrameDisposalMethod.Unspecified, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, delay,
        FrameDisposalMethod.Unspecified, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, delay,
        FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(1));
    Assert.That(result.Frames[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
  }

  [Test]
  [Category("Unit")]
  public void DeduplicateFrames_AllDifferent_NoChange() {
    var gif = _CreateTestGif(3); // Each frame has different pixel data
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(3));
    Assert.That(result, Is.SameAs(gif));
  }

  [Test]
  [Category("EdgeCase")]
  public void DeduplicateFrames_SingleFrame_Unchanged() {
    var gif = _CreateTestGif(1);
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(1));
    Assert.That(result, Is.SameAs(gif));
  }

  // --- Compression-Aware Disposal Tests ---

  [Test]
  [Category("Unit")]
  public void CompressionAwareDisposal_PrefersSmallerOutput() {
    // Two frames: frame 0 fills canvas with red, frame 1 fills with green
    // DoNotDispose means canvas stays red, so diff with green is all changed
    // RestoreToBackground means canvas cleared, also all changed
    // Result should not crash and should pick a valid disposal
    var size = new Dimensions(4, 4);
    var palette = new[] { Color.Red, Color.Green, Color.Blue, Color.White };

    var pixels1 = new byte[16];
    Array.Fill(pixels1, (byte)0); // all red
    var pixels2 = new byte[16];
    Array.Fill(pixels2, (byte)1); // all green

    var frames = new[] {
      new Frame(pixels1, size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false),
      new Frame(pixels2, size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var disposals = GifFrameOptimizer.OptimizeDisposalMethodsByCompression(gif);

    Assert.That(disposals.Length, Is.EqualTo(2));
    // Frame 0's disposal should be one of the valid methods
    Assert.That(disposals[0], Is.AnyOf(
      FrameDisposalMethod.DoNotDispose,
      FrameDisposalMethod.RestoreToBackground,
      FrameDisposalMethod.RestoreToPrevious));
    // Last frame is always Unspecified
    Assert.That(disposals[1], Is.EqualTo(FrameDisposalMethod.Unspecified));
  }

  [Test]
  [Category("Unit")]
  public void CompressionAwareDisposal_IdenticalFrames_PicksDoNotDispose() {
    // Two identical frames: DoNotDispose is optimal since next frame's diff is all transparent
    var size = new Dimensions(4, 4);
    var palette = new[] { Color.Red, Color.Green, Color.Blue, Color.White };
    var pixels = new byte[16];
    Array.Fill(pixels, (byte)1);

    var frames = new[] {
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var disposals = GifFrameOptimizer.OptimizeDisposalMethodsByCompression(gif);

    // For identical frames, DoNotDispose should give max transparency in diff
    Assert.That(disposals[0], Is.EqualTo(FrameDisposalMethod.DoNotDispose));
  }

  // --- Frame Differencing Tests ---

  [Test]
  [Category("Unit")]
  public void ComputeDiffs_IdenticalFrames_AllTransparentExceptFirst() {
    var size = new Dimensions(4, 4);
    var palette = new[] { Color.Red, Color.Green, Color.Blue, Color.White };
    var pixels = new byte[16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 1; // all green

    var frames = new[] {
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.DoNotDispose, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.DoNotDispose, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var result = GifFrameDifferencer.ComputeDiffs(gif);

    Assert.That(result.Length, Is.EqualTo(2));
    // Frame 0 should be unchanged
    Assert.That(result[0].IndexedPixels, Is.EqualTo(pixels));
    // Frame 1: all pixels should be transparent (since all match canvas)
    var transparentIdx = result[1].TransparentColorIndex;
    Assert.That(transparentIdx, Is.Not.Null);
    for (var i = 0; i < result[1].IndexedPixels.Length; ++i)
      Assert.That(result[1].IndexedPixels[i], Is.EqualTo(transparentIdx!.Value), $"Pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void ComputeDiffs_CompletelyDifferentFrames_NoChange() {
    var size = new Dimensions(4, 4);
    var palette = new[] { Color.Red, Color.Green, Color.Blue, Color.White };

    var pixels1 = new byte[16];
    var pixels2 = new byte[16];
    for (var i = 0; i < 16; ++i) {
      pixels1[i] = 0; // all red
      pixels2[i] = 2; // all blue
    }

    var frames = new[] {
      new Frame(pixels1, size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.DoNotDispose, null, false),
      new Frame(pixels2, size, new Offset(0, 0), palette, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.DoNotDispose, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var result = GifFrameDifferencer.ComputeDiffs(gif);

    Assert.That(result.Length, Is.EqualTo(2));
    // Frame 1: no pixels match canvas, so all original indices should remain
    for (var i = 0; i < result[1].IndexedPixels.Length; ++i)
      Assert.That(result[1].IndexedPixels[i], Is.EqualTo(2), $"Pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void EnsureTransparentIndex_NoExisting_ReservesSlot() {
    var palette = new[] { Color.Red, Color.Green, Color.Blue };
    var (idx, newPalette) = GifFrameDifferencer._EnsureTransparentIndex(palette, null);

    Assert.That(idx, Is.EqualTo(3)); // appended after existing 3 entries
    Assert.That(newPalette.Length, Is.EqualTo(4));
    Assert.That(newPalette[0], Is.EqualTo(Color.Red));
    Assert.That(newPalette[1], Is.EqualTo(Color.Green));
    Assert.That(newPalette[2], Is.EqualTo(Color.Blue));
  }

  [Test]
  [Category("Unit")]
  public void EnsureTransparentIndex_Existing_ReturnsExisting() {
    var palette = new[] { Color.Red, Color.Green, Color.Blue };
    var (idx, newPalette) = GifFrameDifferencer._EnsureTransparentIndex(palette, 1);

    Assert.That(idx, Is.EqualTo(1));
    Assert.That(newPalette, Is.SameAs(palette));
  }

  // --- Dedup Palette Bug Regression Tests ---

  [Test]
  [Category("Regression")]
  public void DeduplicateFrames_SwappedPalettes_SameVisual_Deduplicates() {
    var size = new Dimensions(2, 2);
    var paletteA = new[] { Color.Red, Color.Green };
    var paletteB = new[] { Color.Green, Color.Red };
    var pixelsA = new byte[] { 0, 1, 0, 1 }; // Red, Green, Red, Green
    var pixelsB = new byte[] { 1, 0, 1, 0 }; // Red, Green, Red, Green (via swapped palette)

    var frames = new[] {
      new Frame(pixelsA, size, new Offset(0, 0), paletteA, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false),
      new Frame(pixelsB, size, new Offset(0, 0), paletteB, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(1));
    Assert.That(result.Frames[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(200)));
  }

  [Test]
  [Category("Regression")]
  public void DeduplicateFrames_GctVsLct_SameColors_Deduplicates() {
    var size = new Dimensions(2, 2);
    var gct = new[] { Color.Red, Color.Green, Color.Blue, Color.White };
    var pixels = new byte[] { 0, 1, 2, 3 };

    // Frame 0 uses GCT (null LCT), Frame 1 uses identical LCT
    var frames = new[] {
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), null, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), (Color[])gct.Clone(),
        TimeSpan.FromMilliseconds(100), FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, gct, LoopCount.NotSet, 0, frames);
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(1));
    Assert.That(result.Frames[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(200)));
  }

  [Test]
  [Category("Regression")]
  public void DeduplicateFrames_SwappedPalettes_DifferentVisual_DoesNotDeduplicate() {
    var size = new Dimensions(2, 2);
    var paletteA = new[] { Color.Red, Color.Green };
    var paletteB = new[] { Color.Green, Color.Blue };
    var pixels = new byte[] { 0, 1, 0, 1 };

    var frames = new[] {
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), paletteA, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false),
      new Frame((byte[])pixels.Clone(), size, new Offset(0, 0), paletteB, TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified, null, false)
    };

    var gif = new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
    var result = GifFrameOptimizer.DeduplicateFrames(gif);

    Assert.That(result.Frames.Count, Is.EqualTo(2));
    Assert.That(result, Is.SameAs(gif));
  }

  private static GifFile _CreateTestGif(int frameCount) {
    var size = new Dimensions(4, 4);
    var frames = new Frame[frameCount];

    for (var i = 0; i < frameCount; ++i) {
      var pixels = new byte[16];
      for (var j = 0; j < pixels.Length; ++j)
        pixels[j] = (byte)((j + i) % 4);

      frames[i] = new Frame(
        pixels, size, new Offset(0, 0),
        [Color.Red, Color.Green, Color.Blue, Color.White],
        TimeSpan.FromMilliseconds(100),
        FrameDisposalMethod.Unspecified,
        null, false
      );
    }

    return new GifFile("89a", size, null, LoopCount.NotSet, 0, frames);
  }
}
