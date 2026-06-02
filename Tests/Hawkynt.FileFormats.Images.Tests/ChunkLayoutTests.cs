using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Tests for the public chunk-layout / rewrite API on the meta-package <see cref="FormatRegistry"/>.
/// Drives the PNG implementation through the public surface — any new format that adopts
/// <see cref="IFormatChunkLayout{TSelf}"/> + <see cref="IFormatChunkRewriter{TSelf}"/> should grow a parallel
/// fixture next to this one.</summary>
[TestFixture]
public sealed class ChunkLayoutTests {

  private static readonly byte[] _PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  // ---- helpers ----

  private static byte[] _BuildPng(params (string Type, byte[] Data)[] chunks) {
    using var ms = new MemoryStream();
    ms.Write(_PngSignature);
    _WriteChunk(ms, "IHDR", _Ihdr(1, 1));
    foreach (var (type, data) in chunks)
      _WriteChunk(ms, type, data);
    _WriteChunk(ms, "IDAT", [0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x00, 0x04, 0x00, 0x01]);
    _WriteChunk(ms, "IEND", []);
    return ms.ToArray();
  }

  /// <summary>Builds a PNG where each (type, data) entry is written in the given order — caller
  /// controls the relationship to IDAT directly. Used to test moves that need a specific starting
  /// arrangement (e.g. metadata after IDAT).</summary>
  private static byte[] _BuildPngExplicit(params (string Type, byte[] Data)[] chunks) {
    using var ms = new MemoryStream();
    ms.Write(_PngSignature);
    foreach (var (type, data) in chunks)
      _WriteChunk(ms, type, data);
    return ms.ToArray();
  }

  private static byte[] _Ihdr(int width, int height) {
    var data = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)height);
    data[8] = 8;  // bit depth
    data[9] = 2;  // RGB
    return data;
  }

  private static void _WriteChunk(Stream s, string type, byte[] data) {
    System.Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)data.Length);
    s.Write(buf);
    s.Write(Encoding.ASCII.GetBytes(type));
    s.Write(data);
    s.Write(stackalloc byte[4]); // placeholder CRC — none of the tests verify it
  }

  private static List<string> _ChunkOrder(byte[] data)
    => FormatRegistry.EnumerateChunks(data).Select(c => c.Name).Where(n => n != "SIGNATURE").ToList();

  // ---- enumerate ----

  [Test]
  public void EnumerateChunks_Png_ReturnsSignatureFirst() {
    var data = _BuildPng();
    var chunks = FormatRegistry.EnumerateChunks(data);
    Assert.That(chunks, Is.Not.Empty);
    Assert.That(chunks[0].Name, Is.EqualTo("SIGNATURE"));
    Assert.That(chunks[0].Kind, Is.EqualTo(ChunkKind.Signature));
    Assert.That(chunks[0].Offset, Is.EqualTo(0));
    Assert.That(chunks[0].Length, Is.EqualTo(8));
  }

  [Test]
  public void EnumerateChunks_Png_HasIhdrIdatIend() {
    var data = _BuildPng();
    var names = _ChunkOrder(data);
    Assert.That(names, Contains.Item("IHDR"));
    Assert.That(names, Contains.Item("IDAT"));
    Assert.That(names, Contains.Item("IEND"));
  }

  [Test]
  public void EnumerateChunks_Png_OffsetsAreContiguous() {
    var data = _BuildPng(("eXIf", new byte[] { 1, 2, 3 }), ("tEXt", Encoding.ASCII.GetBytes("Key\0Val")));
    var chunks = FormatRegistry.EnumerateChunks(data);
    for (var i = 1; i < chunks.Count; ++i)
      Assert.That(chunks[i].Offset, Is.EqualTo(chunks[i - 1].Offset + chunks[i - 1].Length),
        $"gap between {chunks[i - 1].Name} and {chunks[i].Name}");
  }

  [Test]
  public void EnumerateChunks_Png_OrdinalsDistinguishRepeatedChunks() {
    var data = _BuildPngExplicit(
      ("IHDR", _Ihdr(1, 1)),
      ("IDAT", new byte[] { 1, 2, 3 }),
      ("IDAT", new byte[] { 4, 5, 6 }),
      ("IDAT", new byte[] { 7, 8, 9 }),
      ("IEND", []));
    var idats = FormatRegistry.EnumerateChunks(data).Where(c => c.Name == "IDAT").ToList();
    Assert.That(idats, Has.Count.EqualTo(3));
    Assert.That(idats.Select(c => c.Ordinal), Is.EqualTo(new[] { 0, 1, 2 }));
  }

  [Test]
  public void EnumerateChunks_Png_ClassifiesKindAndMobility() {
    var data = _BuildPng(("eXIf", new byte[] { 1 }), ("gAMA", new byte[4]));
    var byName = FormatRegistry.EnumerateChunks(data).ToDictionary(c => c.Name, c => c);

    Assert.That(byName["IHDR"].Mobility, Is.EqualTo(ChunkMobility.Fixed));
    Assert.That(byName["IEND"].Mobility, Is.EqualTo(ChunkMobility.Fixed));
    Assert.That(byName["IDAT"].Mobility & ChunkMobility.Fusible, Is.Not.EqualTo((ChunkMobility)0));
    Assert.That(byName["eXIf"].Mobility & ChunkMobility.Removable, Is.Not.EqualTo((ChunkMobility)0));
    Assert.That(byName["gAMA"].Mobility & ChunkMobility.Removable, Is.Not.EqualTo((ChunkMobility)0));
    Assert.That(byName["eXIf"].Kind, Is.EqualTo(ChunkKind.Metadata));
  }

  [Test]
  public void EnumerateChunks_UnknownFormat_ReturnsEmpty() {
    var chunks = FormatRegistry.EnumerateChunks(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
    Assert.That(chunks, Is.Empty);
  }

  // ---- rewrite ----

  [Test]
  public void RewriteChunks_Png_MovesMetadataBeforeIdat() {
    var data = _BuildPngExplicit(
      ("IHDR", _Ihdr(1, 1)),
      ("IDAT", new byte[] { 1, 2, 3 }),
      ("eXIf", new byte[] { 4, 5, 6 }),
      ("tEXt", Encoding.ASCII.GetBytes("Key\0Val")),
      ("IEND", []));
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("eXIf", ChunkPlacement.BeforeData),
      new ChunkRewriteRule("tEXt", ChunkPlacement.BeforeData),
    ]);

    var order = _ChunkOrder(rewritten);
    var idatIdx = order.IndexOf("IDAT");
    Assert.That(order.IndexOf("eXIf"), Is.LessThan(idatIdx));
    Assert.That(order.IndexOf("tEXt"), Is.LessThan(idatIdx));
    Assert.That(order[0], Is.EqualTo("IHDR"));
    Assert.That(order[^1], Is.EqualTo("IEND"));
  }

  [Test]
  public void RewriteChunks_Png_MovesMetadataAfterIdat() {
    var data = _BuildPng(("eXIf", new byte[] { 1 }), ("tEXt", Encoding.ASCII.GetBytes("K\0V")));
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("eXIf", ChunkPlacement.AfterData),
      new ChunkRewriteRule("tEXt", ChunkPlacement.AfterData),
    ]);

    var order = _ChunkOrder(rewritten);
    var idatIdx = order.IndexOf("IDAT");
    Assert.That(order.IndexOf("eXIf"), Is.GreaterThan(idatIdx));
    Assert.That(order.IndexOf("tEXt"), Is.GreaterThan(idatIdx));
    Assert.That(order[^1], Is.EqualTo("IEND"));
  }

  [Test]
  public void RewriteChunks_Png_RemovesAncillary() {
    var data = _BuildPng(("eXIf", new byte[] { 1 }), ("tEXt", Encoding.ASCII.GetBytes("K\0V")));
    var original = data.Length;
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("eXIf", ChunkPlacement.Remove),
    ]);

    var order = _ChunkOrder(rewritten);
    Assert.That(order, Does.Not.Contain("eXIf"));
    Assert.That(order, Contains.Item("tEXt"));
    Assert.That(rewritten.Length, Is.LessThan(original));
  }

  [Test]
  public void RewriteChunks_Png_KeepsCriticalChunksDespiteRemove() {
    var data = _BuildPng();
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("IDAT", ChunkPlacement.Remove),
      new ChunkRewriteRule("IHDR", ChunkPlacement.Remove),
      new ChunkRewriteRule("IEND", ChunkPlacement.Remove),
    ]);

    var order = _ChunkOrder(rewritten);
    Assert.That(order, Contains.Item("IHDR"), "IHDR must survive — mobility=Fixed");
    Assert.That(order, Contains.Item("IDAT"), "IDAT survives Remove (it's never marked Removable)");
    Assert.That(order, Contains.Item("IEND"));
  }

  [Test]
  public void RewriteChunks_Png_FusesIdats() {
    var data = _BuildPngExplicit(
      ("IHDR", _Ihdr(1, 1)),
      ("IDAT", new byte[] { 1, 2, 3 }),
      ("IDAT", new byte[] { 4, 5, 6 }),
      ("IDAT", new byte[] { 7, 8, 9, 10 }),
      ("IEND", []));

    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("IDAT", ChunkPlacement.Fuse),
    ]);

    var idats = FormatRegistry.EnumerateChunks(rewritten).Where(c => c.Name == "IDAT").ToList();
    Assert.That(idats, Has.Count.EqualTo(1), "Fused IDATs should collapse to one chunk");
    // Fused data length = 3 + 3 + 4 = 10 bytes; chunk total = 12 + 10 = 22.
    Assert.That(idats[0].Length, Is.EqualTo(22));
  }

  [Test]
  public void RewriteChunks_Png_PreservesIdatDataAcrossReordering() {
    // The byte payload of IDAT must survive moves (this is the "did the writer wreck pixel data?" check).
    var idatPayload = new byte[] { 0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x00, 0x04, 0x00, 0x01 };
    var data = _BuildPngExplicit(
      ("IHDR", _Ihdr(1, 1)),
      ("IDAT", idatPayload),
      ("eXIf", new byte[] { 42 }),
      ("IEND", []));

    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("eXIf", ChunkPlacement.BeforeData),
    ]);

    // Extract IDAT bytes from rewritten and compare to original payload.
    var idat = FormatRegistry.EnumerateChunks(rewritten).First(c => c.Name == "IDAT");
    var dataSlice = rewritten.AsSpan((int)(idat.Offset + 8), (int)(idat.Length - 12)).ToArray();
    Assert.That(dataSlice, Is.EqualTo(idatPayload));
  }

  [Test]
  public void RewriteChunks_NoRules_ReturnsCopy() {
    var data = _BuildPng(("eXIf", new byte[] { 1 }));
    var rewritten = FormatRegistry.RewriteChunks(data, System.Array.Empty<ChunkRewriteRule>());
    Assert.That(rewritten, Is.EqualTo(data));
    Assert.That(rewritten, Is.Not.SameAs(data));
  }

  [Test]
  public void RewriteChunks_UnknownFormat_ReturnsCopyUnchanged() {
    var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("eXIf", ChunkPlacement.Remove),
    ]);
    Assert.That(rewritten, Is.EqualTo(data));
  }

  // ---- entry-level capability flags ----

  [Test]
  public void FormatEntry_Png_AdvertisesChunkLayoutAndRewrite() {
    var png = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(png, Is.Not.Null);
    Assert.That(png!.SupportsChunkLayout, Is.True);
    Assert.That(png.SupportsChunkRewrite, Is.True);
  }
}
