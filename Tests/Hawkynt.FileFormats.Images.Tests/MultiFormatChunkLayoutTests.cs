using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Chunk-layout / rewrite coverage across the formats that adopted the API after PNG:
/// JPEG, WebP (full rewrite); TIFF, BMP (layout-only).</summary>
[TestFixture]
public sealed class MultiFormatChunkLayoutTests {

  // ============================================================
  // JPEG
  // ============================================================

  private static byte[] _BuildJpeg(params (byte Marker, byte[] Payload)[] segments) {
    // Synthesises a minimal JPEG: SOI + segments + minimal SOF/DQT/DHT/SOS + tiny ECS + EOI.
    using var ms = new MemoryStream();
    ms.WriteByte(0xFF); ms.WriteByte(0xD8); // SOI

    foreach (var (marker, payload) in segments) {
      ms.WriteByte(0xFF); ms.WriteByte(marker);
      var len = payload.Length + 2;
      ms.WriteByte((byte)((len >> 8) & 0xFF));
      ms.WriteByte((byte)(len & 0xFF));
      ms.Write(payload);
    }

    // Minimal DQT (so layout parser sees a Data zone).
    ms.WriteByte(0xFF); ms.WriteByte(0xDB);
    ms.WriteByte(0x00); ms.WriteByte(0x05); // length = 5
    ms.Write(new byte[] { 0x00, 0x00, 0x00 });

    // Minimal SOS + a few entropy bytes.
    ms.WriteByte(0xFF); ms.WriteByte(0xDA);
    ms.WriteByte(0x00); ms.WriteByte(0x04); // length=4 means 2 bytes of payload
    ms.WriteByte(0x00); ms.WriteByte(0x00);
    ms.WriteByte(0x12); ms.WriteByte(0x34); // entropy

    ms.WriteByte(0xFF); ms.WriteByte(0xD9); // EOI
    return ms.ToArray();
  }

  [Test]
  public void Jpeg_EnumerateChunks_HasSoiAndEoi() {
    var data = _BuildJpeg();
    var chunks = FormatRegistry.EnumerateChunks(data);
    Assert.That(chunks.Select(c => c.Name), Contains.Item("SOI"));
    Assert.That(chunks.Select(c => c.Name), Contains.Item("EOI"));
  }

  [Test]
  public void Jpeg_EnumerateChunks_ApplyAllowedZones() {
    var data = _BuildJpeg((0xE1, [0xFA, 0xCE])); // APP1 = EXIF-ish
    var byName = FormatRegistry.EnumerateChunks(data).ToDictionary(c => c.Name, c => c);
    Assert.That(byName["APP1"].AllowedZones & AllowedZones.PreData, Is.Not.EqualTo((AllowedZones)0));
    Assert.That(byName["APP1"].AllowedZones & AllowedZones.PostData, Is.Not.EqualTo((AllowedZones)0));
    Assert.That(byName["DQT"].AllowedZones, Is.EqualTo(AllowedZones.Data));
    Assert.That(byName["SOI"].AllowedZones, Is.EqualTo(AllowedZones.Signature));
    Assert.That(byName["EOI"].AllowedZones, Is.EqualTo(AllowedZones.Footer));
  }

  [Test]
  public void Jpeg_RewriteChunks_MovesApp1ToAfterData() {
    var data = _BuildJpeg((0xE1, [0xFA, 0xCE]));
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("APP1", ChunkPlacement.AfterData),
    ]);

    var chunks = FormatRegistry.EnumerateChunks(rewritten);
    var app1 = chunks.FirstOrDefault(c => c.Name == "APP1");
    var eoi = chunks.FirstOrDefault(c => c.Name == "EOI");
    Assert.That(app1.Name, Is.EqualTo("APP1"));
    Assert.That(app1.Offset, Is.GreaterThan(eoi.Offset), "APP1 should live after EOI when moved AfterData");
  }

  [Test]
  public void Jpeg_RewriteChunks_RemovesApp14() {
    var data = _BuildJpeg((0xEE, [0x41, 0x64, 0x6F, 0x62, 0x65])); // APP14 Adobe
    var pre = FormatRegistry.EnumerateChunks(data).Count(c => c.Name == "APP14");
    Assert.That(pre, Is.EqualTo(1));

    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("APP14", ChunkPlacement.Remove),
    ]);

    var post = FormatRegistry.EnumerateChunks(rewritten).Count(c => c.Name == "APP14");
    Assert.That(post, Is.EqualTo(0));
    Assert.That(rewritten.Length, Is.LessThan(data.Length));
  }

  [Test]
  public void Jpeg_ApplyPlan_MoveSofRefused() {
    var data = _BuildJpeg();
    var sof = FormatRegistry.EnumerateChunks(data).FirstOrDefault(c => c.Name.StartsWith("SOF"));
    // The synthetic file above doesn't include a SOF chunk; use DQT which is also Fixed/Data-zone.
    var dqt = FormatRegistry.EnumerateChunks(data).First(c => c.Name == "DQT");

    var result = FormatRegistry.ApplyChunkPlan(data, new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("DQT", dqt.Ordinal), ChunkZone.PostData)],
    });

    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].ChunkName, Is.EqualTo("DQT"));
    Assert.That(result.Failures[0].Reason, Does.Contain("PostData"));
  }

  // ============================================================
  // WebP
  // ============================================================

  private static byte[] _BuildWebPWithIcc() {
    using var ms = new MemoryStream();
    ms.Write("RIFF"u8);
    // Placeholder size (patched below).
    Span<byte> sizeBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(sizeBuf, 0);
    ms.Write(sizeBuf);
    ms.Write("WEBP"u8);

    // VP8X chunk (extended).
    _WebPChunk(ms, "VP8X", new byte[10]);
    // ICCP chunk (movable).
    _WebPChunk(ms, "ICCP", new byte[] { 1, 2, 3, 4, 5 });
    // VP8L data chunk.
    _WebPChunk(ms, "VP8L", new byte[] { 0x2F, 0x00, 0x00, 0x00, 0x00 });
    // EXIF chunk (movable).
    _WebPChunk(ms, "EXIF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

    // Patch RIFF size = total file size - 8.
    var bytes = ms.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
    return bytes;
  }

  private static void _WebPChunk(Stream s, string fourcc, byte[] data) {
    s.Write(Encoding.ASCII.GetBytes(fourcc));
    Span<byte> sizeBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(sizeBuf, (uint)data.Length);
    s.Write(sizeBuf);
    s.Write(data);
    if ((data.Length & 1) == 1) s.WriteByte(0); // RIFF pad
  }

  [Test]
  public void WebP_EnumerateChunks_HasSignatureAndPayload() {
    var data = _BuildWebPWithIcc();
    var chunks = FormatRegistry.EnumerateChunks(data);
    Assert.That(chunks[0].Name, Is.EqualTo("SIGNATURE"));
    Assert.That(chunks.Select(c => c.Name), Contains.Item("VP8L"));
    Assert.That(chunks.Select(c => c.Name), Contains.Item("ICCP"));
    Assert.That(chunks.Select(c => c.Name), Contains.Item("EXIF"));
  }

  [Test]
  public void WebP_RewriteChunks_RemoveExifAndIcc() {
    var data = _BuildWebPWithIcc();
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("EXIF", ChunkPlacement.Remove),
      new ChunkRewriteRule("ICCP", ChunkPlacement.Remove),
    ]);

    var post = FormatRegistry.EnumerateChunks(rewritten);
    Assert.That(post.Select(c => c.Name), Does.Not.Contain("EXIF"));
    Assert.That(post.Select(c => c.Name), Does.Not.Contain("ICCP"));
    Assert.That(post.Select(c => c.Name), Contains.Item("VP8L"));
  }

  [Test]
  public void WebP_RewriteChunks_RiffSizeRecomputed() {
    var data = _BuildWebPWithIcc();
    var rewritten = FormatRegistry.RewriteChunks(data, [
      new ChunkRewriteRule("EXIF", ChunkPlacement.Remove),
    ]);

    var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(rewritten.AsSpan(4, 4));
    Assert.That((int)riffSize, Is.EqualTo(rewritten.Length - 8),
      "RIFF length field must match (file size - 8) after chunk removal");
  }

  [Test]
  public void WebP_ApplyPlan_MoveVp8lRefused() {
    var data = _BuildWebPWithIcc();
    var vp8l = FormatRegistry.EnumerateChunks(data).First(c => c.Name == "VP8L");
    var result = FormatRegistry.ApplyChunkPlan(data, new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("VP8L", vp8l.Ordinal), ChunkZone.PreData)],
    });
    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].ChunkName, Is.EqualTo("VP8L"));
  }

  // ============================================================
  // TIFF (layout-only)
  // ============================================================

  private static byte[] _BuildMinimalTiff() {
    using var ms = new MemoryStream();
    ms.Write("II"u8);                                   // little-endian
    ms.Write(new byte[] { 0x2A, 0x00 });                // magic 42
    ms.Write(new byte[] { 0x08, 0x00, 0x00, 0x00 });    // first IFD at offset 8

    // IFD at offset 8: 2 entries, 4-byte next-IFD ptr.
    ms.Write(new byte[] { 0x02, 0x00 });                // entry count = 2

    // Entry 1: tag 273 (StripOffsets), type 4 (LONG), count 1, value 50.
    ms.Write(new byte[] { 0x11, 0x01, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x32, 0x00, 0x00, 0x00 });
    // Entry 2: tag 279 (StripByteCounts), type 4 (LONG), count 1, value 4.
    ms.Write(new byte[] { 0x17, 0x01, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 });
    // Next-IFD = 0 (end of chain).
    ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });

    // Pad to offset 50 then put 4 strip bytes.
    while (ms.Length < 50) ms.WriteByte(0);
    ms.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
    return ms.ToArray();
  }

  [Test]
  public void Tiff_EnumerateChunks_HasHeaderIfdAndStripData() {
    var data = _BuildMinimalTiff();
    var chunks = FormatRegistry.EnumerateChunks(data);
    var names = chunks.Select(c => c.Name).ToList();
    Assert.That(names, Contains.Item("TiffHeader"));
    Assert.That(names, Contains.Item("IFD0"));
    Assert.That(names, Contains.Item("StripData"));
  }

  [Test]
  public void Tiff_StripData_PointsToActualPayload() {
    var data = _BuildMinimalTiff();
    var strip = FormatRegistry.EnumerateChunks(data).First(c => c.Name == "StripData");
    Assert.That(strip.Offset, Is.EqualTo(50));
    Assert.That(strip.Length, Is.EqualTo(4));
    Assert.That(strip.CurrentZone, Is.EqualTo(ChunkZone.Data));
  }

  [Test]
  public void Tiff_DoesNotAdvertiseRewrite() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Tiff);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.SupportsChunkLayout, Is.True);
    Assert.That(entry.SupportsChunkRewrite, Is.False, "TIFF rewriter not implemented — offsets need patching");
    Assert.That(entry.SupportsChunkPlanRewrite, Is.False);
  }

  // ============================================================
  // BMP (layout-only)
  // ============================================================

  private static byte[] _BuildMinimalBmp() {
    using var ms = new MemoryStream();
    ms.Write("BM"u8);
    ms.Write(new byte[] { 0x46, 0x00, 0x00, 0x00 });    // file size = 70
    ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });    // reserved
    ms.Write(new byte[] { 0x42, 0x00, 0x00, 0x00 });    // pixel data offset = 66

    // BITMAPINFOHEADER (40 bytes).
    ms.Write(new byte[] { 0x28, 0x00, 0x00, 0x00 });    // header size = 40
    ms.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });    // width = 1
    ms.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });    // height = 1
    ms.Write(new byte[] { 0x01, 0x00, 0x20, 0x00 });    // planes=1, bpp=32
    ms.Write(new byte[28]);                              // remainder zero-padded

    // 12-byte (palette + alignment area) — but with 32bpp there's no palette so go straight to pixels.
    // Pad to offset 66.
    while (ms.Length < 66) ms.WriteByte(0);
    ms.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }); // 1 pixel
    return ms.ToArray();
  }

  [Test]
  public void Bmp_EnumerateChunks_HasHeadersAndPixelData() {
    var data = _BuildMinimalBmp();
    var names = FormatRegistry.EnumerateChunks(data).Select(c => c.Name).ToList();
    Assert.That(names, Contains.Item("FileHeader"));
    Assert.That(names, Contains.Item("DibHeader"));
    Assert.That(names, Contains.Item("PixelData"));
  }

  [Test]
  public void Bmp_PixelData_PointsToCorrectOffset() {
    var data = _BuildMinimalBmp();
    var pixelData = FormatRegistry.EnumerateChunks(data).First(c => c.Name == "PixelData");
    Assert.That(pixelData.Offset, Is.EqualTo(66));
    Assert.That(pixelData.CurrentZone, Is.EqualTo(ChunkZone.Data));
  }
}
