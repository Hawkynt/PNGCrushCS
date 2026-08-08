using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.Metadata;

/// <summary>Covers <see cref="ChunkOptimizer.SuggestDeduplicationPlan"/> — the "drop redundant/duplicate
/// blocks" half of the metadata-optimisation requirement, built on the existing chunk-layout substrate
/// (<see cref="FormatRegistry.EnumerateChunks"/> / <see cref="FormatRegistry.ApplyChunkPlan"/>) rather
/// than a parallel mechanism.</summary>
[TestFixture]
public sealed class ChunkOptimizerTests {

  private static readonly byte[] _PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  private static byte[] _BuildPng(params (string Type, byte[] Data)[] extras) {
    using var ms = new MemoryStream();
    ms.Write(_PngSignature);
    _WriteChunk(ms, "IHDR", _Ihdr(1, 1));
    foreach (var (t, d) in extras) _WriteChunk(ms, t, d);
    _WriteChunk(ms, "IDAT", [0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x00, 0x04, 0x00, 0x01]);
    _WriteChunk(ms, "IEND", []);
    return ms.ToArray();
  }

  private static byte[] _Ihdr(int w, int h) {
    var d = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0), (uint)w);
    BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(4), (uint)h);
    d[8] = 8; d[9] = 2;
    return d;
  }

  private static void _WriteChunk(Stream s, string type, byte[] data) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)data.Length);
    s.Write(buf);
    s.Write(Encoding.ASCII.GetBytes(type));
    s.Write(data);
    s.Write(stackalloc byte[4]); // dummy CRC — irrelevant, PngChunkLayout copies bytes verbatim by span.
  }

  [Test]
  public void SuggestDeduplicationPlan_RemovesExactDuplicateTextChunks_KeepsFirst() {
    var duplicateText = Encoding.ASCII.GetBytes("Comment\0same value");
    var data = _BuildPng(("tEXt", duplicateText), ("tEXt", (byte[])duplicateText.Clone()));

    var chunks = FormatRegistry.EnumerateChunks(data);
    var plan = ChunkOptimizer.SuggestDeduplicationPlan(chunks, data);

    Assert.That(plan.Remove, Has.Count.EqualTo(1));
    Assert.That(plan.Remove[0], Is.EqualTo(new ChunkReference("tEXt", 1)));

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.True);
    Assert.That(FormatRegistry.EnumerateChunks(result.Bytes!).Count(c => c.Name == "tEXt"), Is.EqualTo(1));
  }

  [Test]
  public void SuggestDeduplicationPlan_DifferentContent_KeepsBoth() {
    var data = _BuildPng(
      ("tEXt", Encoding.ASCII.GetBytes("Comment\0first")),
      ("tEXt", Encoding.ASCII.GetBytes("Comment\0second")));

    var plan = ChunkOptimizer.SuggestDeduplicationPlan(FormatRegistry.EnumerateChunks(data), data);
    Assert.That(plan.Remove, Is.Empty);
  }

  [Test]
  public void SuggestDeduplicationPlan_PixelDataNeverTargeted() {
    // Even if two IDAT spans happened to hold identical bytes, IDAT is ChunkKind.PixelData, not
    // Metadata — the default kind filter must never suggest touching pixels.
    var data = _BuildPng();
    var plan = ChunkOptimizer.SuggestDeduplicationPlan(FormatRegistry.EnumerateChunks(data), data);
    Assert.That(plan.Remove, Is.Empty);
  }

  [Test]
  public void SuggestDeduplicationPlan_AppliedPlanLeavesPixelDataUntouched() {
    var duplicateExif = new byte[] { 1, 2, 3, 4 };
    var data = _BuildPng(("eXIf", duplicateExif), ("eXIf", (byte[])duplicateExif.Clone()));

    var idatBefore = FormatRegistry.EnumerateChunks(data).First(c => c.Name == "IDAT");
    var idatBytesBefore = data.AsSpan((int)idatBefore.Offset, (int)idatBefore.Length).ToArray();

    var plan = ChunkOptimizer.SuggestDeduplicationPlan(FormatRegistry.EnumerateChunks(data), data);
    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.True);

    var idatAfter = FormatRegistry.EnumerateChunks(result.Bytes!).First(c => c.Name == "IDAT");
    var idatBytesAfter = result.Bytes!.AsSpan((int)idatAfter.Offset, (int)idatAfter.Length).ToArray();
    Assert.That(idatBytesAfter, Is.EqualTo(idatBytesBefore), "deduplication must never re-encode pixel data");
  }

  [Test]
  public void SuggestDeduplicationPlan_ThreeCopies_RemovesTwo() {
    var dup = Encoding.ASCII.GetBytes("k\0v");
    var data = _BuildPng(("tEXt", dup), ("tEXt", (byte[])dup.Clone()), ("tEXt", (byte[])dup.Clone()));

    var plan = ChunkOptimizer.SuggestDeduplicationPlan(FormatRegistry.EnumerateChunks(data), data);
    Assert.That(plan.Remove, Has.Count.EqualTo(2));
  }

  [Test]
  public void SuggestDeduplicationPlan_CombinesWithReordering_MoveSurvivorAfterData() {
    // Prove requirement 5 (reordering) and requirement 4 (dedup) compose: dedupe first, then move the
    // sole survivor into PostData, all through the existing plan-rewrite API.
    var dup = new byte[] { 9, 9, 9 };
    var data = _BuildPng(("eXIf", dup), ("eXIf", (byte[])dup.Clone()));

    var dedupePlan = ChunkOptimizer.SuggestDeduplicationPlan(FormatRegistry.EnumerateChunks(data), data);
    var afterDedupe = FormatRegistry.ApplyChunkPlan(data, dedupePlan);
    Assert.That(afterDedupe.Success, Is.True);

    var movePlan = new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("eXIf", 0), ChunkZone.PostData)],
    };
    var afterMove = FormatRegistry.ApplyChunkPlan(afterDedupe.Bytes!, movePlan);
    Assert.That(afterMove.Success, Is.True);

    var chunks = FormatRegistry.EnumerateChunks(afterMove.Bytes!);
    Assert.That(chunks.Count(c => c.Name == "eXIf"), Is.EqualTo(1));
    Assert.That(chunks.First(c => c.Name == "eXIf").CurrentZone, Is.EqualTo(ChunkZone.PostData));
  }
}
