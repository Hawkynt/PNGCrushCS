using System;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>Cropping a frame to the rectangle that actually carries information, which is where most
/// of an animation's size goes when consecutive frames differ in a small region.</summary>
[TestFixture]
public sealed class FrameWindowTests {

  [Test]
  public void Trim_CropsToBoundingBox_AndShiftsThePosition() {
    // 4x4, background 0, a 2x1 mark at (1,2).
    byte[] pixels = [
      0, 0, 0, 0,
      0, 0, 0, 0,
      0, 5, 6, 0,
      0, 0, 0, 0,
    ];

    var (trimmed, size, position) = GifFrameWindow.Trim(pixels, new Dimensions(4, 4), new Offset(10, 20), 0);

    Assert.That(size, Is.EqualTo(new Dimensions(2, 1)));
    Assert.That(position, Is.EqualTo(new Offset(11, 22)));
    Assert.That(trimmed, Is.EqualTo(new byte[] { 5, 6 }));
  }

  [Test]
  public void Trim_MultiRowBox_CopiesEveryRow() {
    byte[] pixels = [
      7, 7, 7, 7,
      7, 1, 2, 7,
      7, 3, 4, 7,
      7, 7, 7, 7,
    ];

    var (trimmed, size, position) = GifFrameWindow.Trim(pixels, new Dimensions(4, 4), Offset.None, 7);

    Assert.That(size, Is.EqualTo(new Dimensions(2, 2)));
    Assert.That(position, Is.EqualTo(new Offset(1, 1)));
    Assert.That(trimmed, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
  }

  [Test]
  public void Trim_NothingToTrim_ReturnsTheSameArray() {
    byte[] pixels = [1, 2, 3, 4];
    var (trimmed, size, position) = GifFrameWindow.Trim(pixels, new Dimensions(2, 2), new Offset(3, 4), 0);

    Assert.That(trimmed, Is.SameAs(pixels));
    Assert.That(size, Is.EqualTo(new Dimensions(2, 2)));
    Assert.That(position, Is.EqualTo(new Offset(3, 4)));
  }

  [Test]
  public void Trim_EverythingSkippable_YieldsTheSmallestLegalFrame() {
    // GIF has no zero-sized frame, so an entirely skippable frame collapses to 1x1.
    byte[] pixels = [3, 3, 3, 3, 3, 3, 3, 3, 3];
    var (trimmed, size, position) = GifFrameWindow.Trim(pixels, new Dimensions(3, 3), new Offset(2, 2), 3);

    Assert.That(size, Is.EqualTo(new Dimensions(1, 1)));
    Assert.That(trimmed, Is.EqualTo(new byte[] { 3 }));
    Assert.That(position, Is.EqualTo(new Offset(2, 2)), "the position is not moved for a collapsed frame");
  }

  [Test]
  public void Trim_ZeroSizedInput_IsReturnedUnchanged() {
    byte[] pixels = [];
    var (trimmed, size, _) = GifFrameWindow.Trim(pixels, Dimensions.Empty, Offset.None, 0);
    Assert.That(trimmed, Is.SameAs(pixels));
    Assert.That(size, Is.EqualTo(Dimensions.Empty));
  }

  [Test]
  public void Trim_ShorterThanItsDeclaredSize_IsReturnedUnchanged() {
    byte[] pixels = [1, 2, 3];
    var (trimmed, size, _) = GifFrameWindow.Trim(pixels, new Dimensions(4, 4), Offset.None, 0);
    Assert.That(trimmed, Is.SameAs(pixels));
    Assert.That(size, Is.EqualTo(new Dimensions(4, 4)));
  }

  [Test]
  public void Trim_NullPixels_Throws()
    => Assert.Throws<ArgumentNullException>(() => GifFrameWindow.Trim(null!, new Dimensions(1, 1), Offset.None, 0));

  [Test]
  public void Trim_ThenWrite_ProducesASmallerFrameThatStillDecodes() {
    const int W = 64, H = 64;
    var pixels = new byte[W * H];
    pixels[33 * W + 33] = 1; // a single lit pixel in the middle

    var (trimmed, size, position) = GifFrameWindow.Trim(pixels, new Dimensions(W, H), Offset.None, 0);
    Assert.That(size, Is.EqualTo(new Dimensions(1, 1)));

    var gct = new byte[] { 0, 0, 0, 255, 255, 255 };
    var full = GifWriter.ToBytes(_Wrap(pixels, new Dimensions(W, H), Offset.None, gct, W, H));
    var cropped = GifWriter.ToBytes(_Wrap(trimmed, size, position, gct, W, H));

    Assert.That(cropped.Length, Is.LessThan(full.Length));
    var reread = GifReader.FromBytes(cropped);
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(new byte[] { 1 }));
    Assert.That(reread.Frames[0].Left, Is.EqualTo(33));
    Assert.That(reread.Frames[0].Top, Is.EqualTo(33));
  }

  private static GifFile _Wrap(byte[] pixels, Dimensions size, Offset position, byte[] gct, ushort screenW, ushort screenH) => new() {
    Version = GifVersion.Gif89a,
    LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
      Width: screenW, Height: screenH, HasGlobalColorTable: true, ColorResolution: 8,
      GlobalColorTableSorted: false, GlobalColorTableSize: 0, BackgroundColorIndex: 0, PixelAspectRatio: 0),
    GlobalColorTable = gct,
    LoopCount = LoopCount.PlayOnce,
    Frames = [new Frame {
      Left = position.X, Top = position.Y, Width = size.Width, Height = size.Height,
      PixelData = pixels,
    }],
  };
}
