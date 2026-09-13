using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>The incremental writer. What it exists for is the animation that is generated rather than
/// loaded: <see cref="GifWriter.ToBytes"/> needs every frame's pixels resident before it emits a
/// byte, which for a few hundred full-canvas 8bpp layers is hundreds of megabytes held at once.</summary>
[TestFixture]
public sealed class StreamWriterTests {

  private static readonly byte[] _Palette = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];

  private static GifLogicalScreenDescriptor _Lsd(ushort w, ushort h) => new(
    Width: w, Height: h, HasGlobalColorTable: true, ColorResolution: 8,
    GlobalColorTableSorted: false, GlobalColorTableSize: 1,
    BackgroundColorIndex: 0, PixelAspectRatio: 0);

  private static Frame _Frame(ushort w, ushort h, byte fill, int delayMs) => new() {
    Left = 0, Top = 0, Width = w, Height = h,
    PixelData = _Fill(w * h, fill),
    Delay = TimeSpan.FromMilliseconds(delayMs),
    DisposalMethod = FrameDisposalMethod.DoNotDispose,
  };

  private static byte[] _Fill(int n, byte v) {
    var a = new byte[n];
    for (var i = 0; i < n; ++i) a[i] = v;
    return a;
  }

  [Test]
  public void Streamed_MatchesBatchWriter_ByteForByte() {
    var frames = new[] { _Frame(4, 4, 1, 100), _Frame(4, 4, 2, 100), _Frame(4, 4, 3, 100) };

    var batch = GifWriter.ToBytes(new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = _Lsd(4, 4),
      GlobalColorTable = _Palette,
      LoopCount = LoopCount.LoopForever,
      Frames = frames,
    });

    using var ms = new MemoryStream();
    using (var w = new GifStreamWriter(ms, _Lsd(4, 4), _Palette, LoopCount.LoopForever))
      foreach (var f in frames)
        w.WriteFrame(f);

    Assert.That(ms.ToArray(), Is.EqualTo(batch));
  }

  [Test]
  public void Streamed_RoundTripsEveryFrame() {
    using var ms = new MemoryStream();
    using (var w = new GifStreamWriter(ms, _Lsd(8, 8), _Palette, LoopCount.LoopForever))
      for (byte i = 1; i <= 3; ++i)
        w.WriteFrame(_Frame(8, 8, i, 50));

    var reread = GifReader.FromBytes(ms.ToArray());
    Assert.That(reread.Frames, Has.Count.EqualTo(3));
    Assert.That(reread.LoopCount.IsInfinite, Is.True);
    for (var i = 0; i < 3; ++i)
      Assert.That(reread.Frames[i].PixelData, Is.EqualTo(_Fill(64, (byte)(i + 1))), $"frame {i}");
  }

  [Test]
  public void WriteTo_ConsumesTheSequenceLazily_HoldingOneFrame() {
    var live = 0;
    var peak = 0;

    IEnumerable<Frame> Produce() {
      for (byte i = 1; i <= 5; ++i) {
        ++live;
        if (live > peak) peak = live;
        yield return _Frame(4, 4, i, 40);
        --live; // the writer is done with it by the time control comes back
      }
    }

    using var ms = new MemoryStream();
    Writer.WriteTo(ms, new Dimensions(4, 4), Produce(), LoopCount.LoopForever, globalColorTable: _Palette);

    Assert.That(peak, Is.EqualTo(1), "the writer must not hold more than the frame it is emitting");
    Assert.That(GifReader.FromBytes(ms.ToArray()).Frames, Has.Count.EqualTo(5));
  }

  [Test]
  public void Complete_IsIdempotent_AndDisposeAfterItAddsNoSecondTrailer() {
    using var ms = new MemoryStream();
    var w = new GifStreamWriter(ms, _Lsd(2, 2), _Palette, LoopCount.PlayOnce);
    w.WriteFrame(_Frame(2, 2, 1, 10));
    w.Complete();
    var afterComplete = ms.Length;
    w.Complete();
    w.Dispose();

    Assert.That(ms.Length, Is.EqualTo(afterComplete));
    Assert.That(ms.ToArray()[^1], Is.EqualTo(0x3B));
  }

  [Test]
  public void AppendingAfterComplete_Throws() {
    using var ms = new MemoryStream();
    var w = new GifStreamWriter(ms, _Lsd(2, 2), _Palette, LoopCount.PlayOnce);
    w.Complete();
    Assert.Throws<InvalidOperationException>(() => w.WriteFrame(_Frame(2, 2, 1, 10)));
    Assert.Throws<InvalidOperationException>(() => w.WriteComment([1, 2, 3]));
  }

  [Test]
  public void Gif87aSignature_IsHonoured() {
    using var ms = new MemoryStream();
    using (var w = new GifStreamWriter(ms, _Lsd(2, 2), _Palette, LoopCount.PlayOnce, GifVersion.Gif87a))
      w.WriteFrame(new Frame { Left = 0, Top = 0, Width = 2, Height = 2, PixelData = [0, 1, 1, 0] });

    Assert.That(GifReader.FromBytes(ms.ToArray()).Version, Is.EqualTo(GifVersion.Gif87a));
  }

  [Test]
  public void GlobalTable_IsPaddedByTheStreamingWriterToo() {
    var gct = new byte[] { 9, 9, 9, 8, 8, 8, 7, 7, 7 }; // 3 colours
    using var ms = new MemoryStream();
    using (var w = new GifStreamWriter(ms, _Lsd(2, 2) with { GlobalColorTableSize = 0 }, gct, LoopCount.PlayOnce))
      w.WriteFrame(new Frame { Left = 0, Top = 0, Width = 2, Height = 2, PixelData = [0, 1, 2, 0] });

    var reread = GifReader.FromBytes(ms.ToArray());
    Assert.That(reread.LogicalScreenDescriptor.GlobalColorTableEntryCount, Is.EqualTo(4));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(new byte[] { 0, 1, 2, 0 }));
  }

  [Test]
  public void CommentsAndExtensions_CanBeInterleavedBetweenFrames() {
    using var ms = new MemoryStream();
    using (var w = new GifStreamWriter(ms, _Lsd(2, 2), _Palette, LoopCount.PlayOnce)) {
      w.WriteComment([(byte)'h', (byte)'i']);
      w.WriteFrame(_Frame(2, 2, 1, 10));
      w.WritePlainText(new GifPlainTextExtension(1, 2, 3, 4, 5, 6, 7, 8, [(byte)'t']));
      w.WriteApplicationExtension(new GifApplicationExtension("XMP Data", [(byte)'X', (byte)'M', (byte)'P'], [1, 2]));
      w.WriteFrame(_Frame(2, 2, 2, 10));
    }

    var reread = GifReader.FromBytes(ms.ToArray());
    Assert.That(reread.Frames, Has.Count.EqualTo(2));
    Assert.That(reread.Comments, Has.Count.EqualTo(1));
    Assert.That(reread.Comments[0].Data, Is.EqualTo(new byte[] { (byte)'h', (byte)'i' }));
    Assert.That(reread.PlainTextExtensions, Has.Count.EqualTo(1));
    Assert.That(reread.ApplicationExtensions, Has.Count.EqualTo(1));
  }

  [Test]
  public void NullArguments_Throw() {
    using var ms = new MemoryStream();
    Assert.Throws<ArgumentNullException>(() => new GifStreamWriter(null!, _Lsd(1, 1), _Palette, LoopCount.PlayOnce));
    using var w = new GifStreamWriter(ms, _Lsd(1, 1), _Palette, LoopCount.PlayOnce);
    Assert.Throws<ArgumentNullException>(() => w.WriteFrame(null!));
    Assert.Throws<ArgumentNullException>(() => w.WriteComment(null!));
    Assert.Throws<ArgumentNullException>(() => Writer.WriteTo(null!, new Dimensions(1, 1), [], LoopCount.PlayOnce));
    Assert.Throws<ArgumentNullException>(() => Writer.WriteTo(ms, new Dimensions(1, 1), null!, LoopCount.PlayOnce));
    Assert.Throws<ArgumentNullException>(() => Writer.ToFile(null!, new Dimensions(1, 1), [], LoopCount.PlayOnce));
  }

  [Test]
  public void ToFile_WritesAReadableAnimation() {
    var path = Path.Combine(Path.GetTempPath(), $"gifstream-{Guid.NewGuid():N}.gif");
    try {
      Writer.ToFile(new FileInfo(path), new Dimensions(4, 4),
        [_Frame(4, 4, 1, 100), _Frame(4, 4, 2, 100)],
        LoopCount.LoopForever, globalColorTable: _Palette);

      var reread = GifReader.FromFile(new FileInfo(path));
      Assert.That(reread.Frames, Has.Count.EqualTo(2));
      Assert.That(reread.LoopCount.IsInfinite, Is.True);
    } finally {
      if (File.Exists(path)) File.Delete(path);
    }
  }
}
