using System;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>The surface the external <c>Hawkynt.GifFileFormat</c> package exposed, so a consumer
/// migrating onto this codec finds the same names and the same meanings.</summary>
[TestFixture]
public sealed class ApiParityTests {

  [Test]
  public void LoopCount_Infinite_IsPresentWithCountZero() {
    var infinite = LoopCount.Infinite;
    Assert.That(infinite.IsSet, Is.True);
    Assert.That(infinite.IsInfinite, Is.True);
    Assert.That(infinite.Value, Is.EqualTo(0));
    Assert.That(infinite, Is.EqualTo(LoopCount.LoopForever));
  }

  [Test]
  public void LoopCount_NotSet_IsAbsent() {
    Assert.That(LoopCount.NotSet.IsSet, Is.False);
    Assert.That(LoopCount.NotSet.IsNotSet, Is.True);
    Assert.That(LoopCount.NotSet.IsInfinite, Is.False);
  }

  [Test]
  public void LoopCount_OnceAndTwice_CarryTheirCounts() {
    Assert.That(LoopCount.Once.IsSet, Is.True);
    Assert.That(LoopCount.Once.Value, Is.EqualTo(1));
    Assert.That(LoopCount.Twice.Value, Is.EqualTo(2));
  }

  [Test]
  public void LoopCount_ConvertsFromUshort() {
    LoopCount fromValue = (ushort)10;
    Assert.That(fromValue.IsSet, Is.True);
    Assert.That(fromValue.Value, Is.EqualTo(10));

    LoopCount fromNull = (ushort?)null;
    Assert.That(fromNull.IsSet, Is.False);

    LoopCount fromNullable = (ushort?)7;
    Assert.That(fromNullable.Value, Is.EqualTo(7));
  }

  [Test]
  public void LoopCount_SurvivesAWriteReadRoundTrip() {
    foreach (var loop in new[] { LoopCount.NotSet, LoopCount.Infinite, LoopCount.Once, LoopCount.LoopTimes(1234) }) {
      var file = new GifFile {
        Version = GifVersion.Gif89a,
        LogicalScreenDescriptor = new GifLogicalScreenDescriptor(2, 2, true, 8, false, 0, 0, 0),
        GlobalColorTable = [0, 0, 0, 255, 255, 255],
        LoopCount = loop,
        Frames = [new Frame { Left = 0, Top = 0, Width = 2, Height = 2, PixelData = [0, 1, 1, 0] }],
      };
      var reread = GifReader.FromBytes(GifWriter.ToBytes(file));
      Assert.That(reread.LoopCount.IsSet, Is.EqualTo(loop.IsSet), $"{loop}");
      if (loop.IsSet)
        Assert.That(reread.LoopCount.Value, Is.EqualTo(loop.Value), $"{loop}");
    }
  }

  [Test]
  public void Dimensions_With_TakesTheLargerOfEachAxis() {
    Assert.That(new Dimensions(10, 4).With(new Dimensions(3, 9)), Is.EqualTo(new Dimensions(10, 9)));
    Assert.That(Dimensions.Empty.With(new Dimensions(5, 5)), Is.EqualTo(new Dimensions(5, 5)));
    Assert.That(new Dimensions(5, 5).With(Dimensions.Empty), Is.EqualTo(new Dimensions(5, 5)));
  }

  [Test]
  public void Dimensions_RejectsOutOfRange() {
    Assert.Throws<ArgumentOutOfRangeException>(() => new Dimensions(-1, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => new Dimensions(1, 70000));
  }

  [Test]
  public void Offset_RejectsOutOfRange() {
    Assert.Throws<ArgumentOutOfRangeException>(() => new Offset(-1, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new Offset(0, 70000));
  }

  [Test]
  public void ColorResolution_MapsOntoTheDescriptorField() {
    var lsd = new GifLogicalScreenDescriptor(1, 1, false, 8, false, 0, 0, 0);
    Assert.That(lsd.ColorResolutionValue, Is.EqualTo(ColorResolution.Colored256));

    var mono = lsd.WithColorResolution(ColorResolution.Monochrome);
    Assert.That(mono.ColorResolution, Is.EqualTo(1));
    Assert.That(mono.ColorResolutionValue, Is.EqualTo(ColorResolution.Monochrome));
  }

  [Test]
  public void ColorResolution_SurvivesAWriteReadRoundTrip() {
    foreach (var resolution in new[] { ColorResolution.Monochrome, ColorResolution.Colored16, ColorResolution.Colored256 }) {
      var file = new GifFile {
        Version = GifVersion.Gif89a,
        LogicalScreenDescriptor = new GifLogicalScreenDescriptor(2, 2, true, 8, false, 0, 0, 0)
          .WithColorResolution(resolution),
        GlobalColorTable = [0, 0, 0, 255, 255, 255],
        LoopCount = LoopCount.PlayOnce,
        Frames = [new Frame { Left = 0, Top = 0, Width = 2, Height = 2, PixelData = [0, 1, 1, 0] }],
      };
      var reread = GifReader.FromBytes(GifWriter.ToBytes(file));
      Assert.That(reread.LogicalScreenDescriptor.ColorResolutionValue, Is.EqualTo(resolution));
    }
  }

  [Test]
  public void Frame_WithDelay_ClonesEverythingElse() {
    var frame = new Frame(
      [1, 2, 3, 4], new Dimensions(2, 2), new Offset(5, 6), [0, 0, 0, 1, 1, 1],
      TimeSpan.FromMilliseconds(100), FrameDisposalMethod.DoNotDispose, 1, true);

    var slower = frame.WithDelay(TimeSpan.FromMilliseconds(500));

    Assert.That(slower.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
    Assert.That(slower.IndexedPixels, Is.EqualTo(frame.IndexedPixels));
    Assert.That(slower.Size, Is.EqualTo(frame.Size));
    Assert.That(slower.Position, Is.EqualTo(frame.Position));
    Assert.That(slower.LocalColorTable, Is.EqualTo(frame.LocalColorTable));
    Assert.That(slower.DisposalMethod, Is.EqualTo(frame.DisposalMethod));
    Assert.That(slower.TransparentColorIndex, Is.EqualTo(frame.TransparentColorIndex));
    Assert.That(slower.IsInterlaced, Is.EqualTo(frame.IsInterlaced));
  }
}
