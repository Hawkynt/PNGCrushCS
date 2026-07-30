using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

[TestFixture]
public sealed class ChunkLayoutTests {

  private static byte[] _BuildGifWithExtensions() {
    var palette = new byte[] { 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 2, Height: 2, HasGlobalColorTable: true,
        ColorResolution: 8, GlobalColorTableSorted: false,
        GlobalColorTableSize: 1, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = palette,
      LoopCount = LoopCount.LoopForever,
      Comments = [
        new GifCommentExtension(Encoding.ASCII.GetBytes("Comment One")),
        new GifCommentExtension(Encoding.ASCII.GetBytes("Comment Two")),
      ],
      ApplicationExtensions = [
        new GifApplicationExtension("XMP Data", "XMP"u8.ToArray(), Encoding.UTF8.GetBytes("xmpdata")),
        new GifApplicationExtension("ICCRGBG1", "012"u8.ToArray(), new byte[] { 0xDE, 0xAD }),
      ],
      Frames = [new Frame { Left = 0, Top = 0, Width = 2, Height = 2, PixelData = [0, 1, 2, 3] }],
    };
    return GifWriter.ToBytes(src);
  }

  // GifChunkLayout is internal; FileFormat.Gif.Tests has InternalsVisibleTo to reach it directly.
  private static System.Collections.Generic.IReadOnlyList<ChunkSpan> _Enumerate(byte[] data)
    => GifChunkLayout.Enumerate(data);

  private static byte[] _Rewrite(byte[] data, System.Collections.Generic.IReadOnlyList<ChunkRewriteRule> rules)
    => GifChunkLayout.Rewrite(data, rules);

  private static ChunkRewriteResult _ApplyPlan(byte[] data, ChunkRewritePlan plan)
    => GifChunkLayout.ApplyPlan(data, plan);

  [Test]
  public void Enumerate_HasSignatureGctFramesTrailer() {
    var bytes = _BuildGifWithExtensions();
    var chunks = _Enumerate(bytes);
    var names = chunks.Select(c => c.Name).ToList();
    Assert.That(names, Contains.Item("SIGNATURE"));
    Assert.That(names, Contains.Item("GCT"));
    Assert.That(names, Contains.Item("FRAME"));
    Assert.That(names, Contains.Item("TRAILER"));
  }

  [Test]
  public void Enumerate_DetectsAllExtensionChunks() {
    var bytes = _BuildGifWithExtensions();
    var chunks = _Enumerate(bytes);
    Assert.That(chunks.Count(c => c.Name == "COMMENT"), Is.EqualTo(2));
    Assert.That(chunks.Count(c => c.Name == "APP_XMP"), Is.EqualTo(1));
    Assert.That(chunks.Count(c => c.Name == "APP_ICC"), Is.EqualTo(1));
    Assert.That(chunks.Count(c => c.Name == "APP_NETSCAPE"), Is.EqualTo(1));
  }

  [Test]
  public void Enumerate_CommentIsMovableAndRemovable() {
    var bytes = _BuildGifWithExtensions();
    var comment = _Enumerate(bytes).First(c => c.Name == "COMMENT");
    Assert.That(comment.Mobility & ChunkMobility.Movable, Is.Not.EqualTo((ChunkMobility)0));
    Assert.That(comment.Mobility & ChunkMobility.Removable, Is.Not.EqualTo((ChunkMobility)0));
    Assert.That(comment.AllowedZones & (AllowedZones.PreData | AllowedZones.PostData), Is.EqualTo(AllowedZones.PreData | AllowedZones.PostData));
  }

  [Test]
  public void Enumerate_FrameIsFixedInDataZone() {
    var bytes = _BuildGifWithExtensions();
    var frame = _Enumerate(bytes).First(c => c.Name == "FRAME");
    Assert.That(frame.Mobility, Is.EqualTo(ChunkMobility.Fixed));
    Assert.That(frame.AllowedZones, Is.EqualTo(AllowedZones.Data));
  }

  [Test]
  public void Rewrite_RemovesAllComments() {
    var bytes = _BuildGifWithExtensions();
    var rewritten = _Rewrite(bytes, [new ChunkRewriteRule("COMMENT", ChunkPlacement.Remove)]);
    var post = _Enumerate(rewritten);
    Assert.That(post.Count(c => c.Name == "COMMENT"), Is.EqualTo(0));
    Assert.That(post.Count(c => c.Name == "FRAME"), Is.EqualTo(1)); // frames preserved
    // Round-trip via the full reader still works.
    Assert.That(() => GifReader.FromBytes(rewritten), Throws.Nothing);
  }

  [Test]
  public void Rewrite_RemovesXmp() {
    var bytes = _BuildGifWithExtensions();
    var rewritten = _Rewrite(bytes, [new ChunkRewriteRule("APP_XMP", ChunkPlacement.Remove)]);
    var post = _Enumerate(rewritten);
    Assert.That(post.Count(c => c.Name == "APP_XMP"), Is.EqualTo(0));
    Assert.That(post.Count(c => c.Name == "APP_ICC"), Is.EqualTo(1)); // unrelated extension untouched
  }

  [Test]
  public void Rewrite_MovesCommentsAfterData() {
    var bytes = _BuildGifWithExtensions();
    var rewritten = _Rewrite(bytes, [new ChunkRewriteRule("COMMENT", ChunkPlacement.AfterData)]);
    var post = _Enumerate(rewritten);
    var comments = post.Where(c => c.Name == "COMMENT").ToList();
    var lastFrame = post.Last(c => c.Name == "FRAME");
    foreach (var comment in comments)
      Assert.That(comment.Offset, Is.GreaterThan(lastFrame.Offset),
        "moved comments should sit after the last frame");
  }

  [Test]
  public void ApplyPlan_MoveFrameRefused() {
    var bytes = _BuildGifWithExtensions();
    var frame = _Enumerate(bytes).First(c => c.Name == "FRAME");
    var result = _ApplyPlan(bytes, new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("FRAME", frame.Ordinal), ChunkZone.PreData)],
    });
    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].ChunkName, Is.EqualTo("FRAME"));
  }

  [Test]
  public void ApplyPlan_MoveCommentToPostData_Succeeds() {
    var bytes = _BuildGifWithExtensions();
    var comment = _Enumerate(bytes).First(c => c.Name == "COMMENT");
    var result = _ApplyPlan(bytes, new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("COMMENT", comment.Ordinal), ChunkZone.PostData)],
    });
    Assert.That(result.Success, Is.True);
    Assert.That(() => GifReader.FromBytes(result.Bytes!), Throws.Nothing);
  }
}
