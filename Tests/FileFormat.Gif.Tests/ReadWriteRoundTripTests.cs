using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

[TestFixture]
public sealed class ReadWriteRoundTripTests {

  private static byte[] _BuildSingleFrameGif() {
    var palette = new byte[] {
      0xFF, 0x00, 0x00,           // red
      0x00, 0xFF, 0x00,           // green
      0x00, 0x00, 0xFF,           // blue
      0xFF, 0xFF, 0xFF,           // white
    };
    var pixels = new byte[] { 0, 1, 2, 3, 1, 2, 3, 0, 2, 3, 0, 1, 3, 0, 1, 2 };
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 4, Height: 4, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 1, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = palette,
      Frames = [new Frame {
        Left = 0, Top = 0, Width = 4, Height = 4,
        PixelData = pixels,
      }],
    };
    return GifWriter.ToBytes(src);
  }

  [Test]
  public void RoundTrip_SimpleSingleFrame_PreservesPixels() {
    var input = _BuildSingleFrameGif();
    var gif = GifReader.FromBytes(input);
    Assert.That(gif.Frames.Count, Is.EqualTo(1));
    Assert.That(gif.Frames[0].Width, Is.EqualTo((ushort)4));
    Assert.That(gif.Frames[0].Height, Is.EqualTo((ushort)4));
    var expected = new byte[] { 0, 1, 2, 3, 1, 2, 3, 0, 2, 3, 0, 1, 3, 0, 1, 2 };
    Assert.That(gif.Frames[0].PixelData, Is.EqualTo(expected));
  }

  [Test]
  public void RoundTrip_WriteThenReread_PreservesEverything() {
    var src = GifReader.FromBytes(_BuildSingleFrameGif());
    var bytes = GifWriter.ToBytes(src);
    var reread = GifReader.FromBytes(bytes);
    Assert.That(reread.Frames.Count, Is.EqualTo(src.Frames.Count));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(src.Frames[0].PixelData));
    Assert.That(reread.LogicalScreenDescriptor.Width, Is.EqualTo(src.LogicalScreenDescriptor.Width));
    Assert.That(reread.LogicalScreenDescriptor.Height, Is.EqualTo(src.LogicalScreenDescriptor.Height));
    Assert.That(reread.GlobalColorTable, Is.EqualTo(src.GlobalColorTable));
  }

  [Test]
  public void RoundTrip_AnimationLoop_PreservesLoopCount() {
    var palette = new byte[] { 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };
    var frame = new Frame {
      Left = 0, Top = 0, Width = 2, Height = 2,
      PixelData = [0, 1, 2, 3],
      Delay = TimeSpan.FromMilliseconds(100),
      DisposalMethod = FrameDisposalMethod.RestoreToBackground,
    };
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 2, Height: 2, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 1, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = palette,
      LoopCount = LoopCount.LoopForever,
      Frames = [frame, frame, frame],
    };
    var roundTrip = GifReader.FromBytes(GifWriter.ToBytes(src));
    Assert.That(roundTrip.LoopCount.IsPresent, Is.True);
    Assert.That(roundTrip.LoopCount.IsInfinite, Is.True);
    Assert.That(roundTrip.Frames.Count, Is.EqualTo(3));
    foreach (var f in roundTrip.Frames) {
      Assert.That(f.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
      Assert.That(f.DisposalMethod, Is.EqualTo(FrameDisposalMethod.RestoreToBackground));
    }
  }

  [Test]
  public void RoundTrip_CommentExtension_Preserved() {
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 1, Height: 1, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 0, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = [0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56],
      Comments = [new GifCommentExtension(Encoding.ASCII.GetBytes("CreatedByPNGCrushCS"))],
      Frames = [new Frame { Left = 0, Top = 0, Width = 1, Height = 1, PixelData = [0] }],
    };
    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));
    Assert.That(reread.Comments, Has.Count.EqualTo(1));
    Assert.That(Encoding.ASCII.GetString(reread.Comments[0].Data), Is.EqualTo("CreatedByPNGCrushCS"));
  }

  [Test]
  public void RoundTrip_XmpApplicationExtension_Preserved() {
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 1, Height: 1, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 0, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = [0, 0, 0, 0xFF, 0xFF, 0xFF],
      ApplicationExtensions = [
        new GifApplicationExtension("XMP Data", "XMP"u8.ToArray(), Encoding.UTF8.GetBytes("<x:xmpmeta/>")),
      ],
      Frames = [new Frame { Left = 0, Top = 0, Width = 1, Height = 1, PixelData = [1] }],
    };
    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));
    var xmp = reread.ApplicationExtensions.FirstOrDefault(e => e.Identifier == "XMP Data");
    Assert.That(xmp, Is.Not.Null);
    Assert.That(Encoding.UTF8.GetString(xmp!.Data), Is.EqualTo("<x:xmpmeta/>"));
  }

  [Test]
  public void Reader_TruncatedFile_DoesNotThrow() {
    var full = _BuildSingleFrameGif();
    var truncated = full[..(full.Length - 5)];
    Assert.That(() => GifReader.FromBytes(truncated), Throws.Nothing);
  }

  [Test]
  public void Reader_NotAGif_Throws() {
    Assert.That(() => GifReader.FromBytes("BMP-not-a-gif-at-all"u8.ToArray()),
      Throws.TypeOf<InvalidDataException>());
  }
}
