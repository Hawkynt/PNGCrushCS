using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Tests for the concrete-placement <see cref="FormatRegistry.ApplyChunkPlan(byte[], ChunkRewritePlan)"/>
/// API: per-chunk zone introspection, plan validation, and refusal of file-invalidating moves.</summary>
[TestFixture]
public sealed class ChunkPlanRewriteTests {

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
    s.Write(stackalloc byte[4]); // dummy CRC
  }

  // ---- AllowedZones introspection ----

  [Test]
  public void Enumerate_Png_PopulatesCurrentZone() {
    var data = _BuildPng(("eXIf", new byte[] { 1 }));
    var chunks = FormatRegistry.EnumerateChunks(data);
    var byName = chunks.ToDictionary(c => c.Name, c => c);

    Assert.That(byName["SIGNATURE"].CurrentZone, Is.EqualTo(ChunkZone.Signature));
    Assert.That(byName["IHDR"].CurrentZone, Is.EqualTo(ChunkZone.Header));
    Assert.That(byName["IDAT"].CurrentZone, Is.EqualTo(ChunkZone.Data));
    Assert.That(byName["IEND"].CurrentZone, Is.EqualTo(ChunkZone.Footer));
    Assert.That(byName["eXIf"].CurrentZone, Is.EqualTo(ChunkZone.PreData));
  }

  [Test]
  public void Enumerate_Png_AllowedZonesMatchSpec() {
    var data = _BuildPng(("PLTE", new byte[3]), ("eXIf", new byte[] { 1 }), ("gAMA", new byte[4]));
    var byName = FormatRegistry.EnumerateChunks(data).ToDictionary(c => c.Name, c => c);

    Assert.That(byName["IHDR"].AllowedZones, Is.EqualTo(AllowedZones.Header));
    Assert.That(byName["IEND"].AllowedZones, Is.EqualTo(AllowedZones.Footer));
    Assert.That(byName["IDAT"].AllowedZones, Is.EqualTo(AllowedZones.Data));
    // PLTE must precede IDAT → PreData only.
    Assert.That(byName["PLTE"].AllowedZones, Is.EqualTo(AllowedZones.PreData));
    // gAMA is a display hint → PreData only per PNG spec.
    Assert.That(byName["gAMA"].AllowedZones, Is.EqualTo(AllowedZones.PreData));
    // eXIf has no spec ordering constraint → may live on either side of IDAT.
    Assert.That(byName["eXIf"].AllowedZones & AllowedZones.PreData, Is.Not.EqualTo((AllowedZones)0));
    Assert.That(byName["eXIf"].AllowedZones & AllowedZones.PostData, Is.Not.EqualTo((AllowedZones)0));
  }

  // ---- legal plan succeeds ----

  [Test]
  public void ApplyPlan_MoveExifToPostData_Succeeds() {
    var data = _BuildPng(("eXIf", new byte[] { 1, 2, 3 }));
    var plan = new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("eXIf", 0), ChunkZone.PostData)],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.True);
    Assert.That(result.Failures, Is.Empty);

    var byName = FormatRegistry.EnumerateChunks(result.Bytes!).ToDictionary(c => c.Name, c => c);
    Assert.That(byName["eXIf"].CurrentZone, Is.EqualTo(ChunkZone.PostData));
  }

  [Test]
  public void ApplyPlan_OrderInZoneRespected() {
    var data = _BuildPng(
      ("tEXt", Encoding.ASCII.GetBytes("a\0a")),
      ("eXIf", new byte[] { 1 }),
      ("iTXt", new byte[] { 9 }));
    var plan = new ChunkRewritePlan {
      Placements = [
        new ChunkPlacementDirective(new ChunkReference("iTXt", 0), ChunkZone.PreData, OrderInZone: 0),
        new ChunkPlacementDirective(new ChunkReference("eXIf", 0), ChunkZone.PreData, OrderInZone: 1),
        new ChunkPlacementDirective(new ChunkReference("tEXt", 0), ChunkZone.PreData, OrderInZone: 2),
      ],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.True);

    var order = FormatRegistry.EnumerateChunks(result.Bytes!)
      .Where(c => c.Name is "iTXt" or "eXIf" or "tEXt")
      .Select(c => c.Name).ToList();
    Assert.That(order, Is.EqualTo(new[] { "iTXt", "eXIf", "tEXt" }));
  }

  // ---- illegal plan is refused ----

  [Test]
  public void ApplyPlan_MovePlteToPostData_Refused() {
    var data = _BuildPng(("PLTE", new byte[3]));
    var plan = new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("PLTE", 0), ChunkZone.PostData)],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.False);
    Assert.That(result.Bytes, Is.Null);
    Assert.That(result.Failures, Has.Count.GreaterThan(0));
    Assert.That(result.Failures[0].Operation, Is.EqualTo("Place"));
    Assert.That(result.Failures[0].ChunkName, Is.EqualTo("PLTE"));
    Assert.That(result.Failures[0].Reason, Does.Contain("PostData"));
  }

  [Test]
  public void ApplyPlan_RemoveIhdr_Refused() {
    var data = _BuildPng();
    var plan = new ChunkRewritePlan {
      Remove = [new ChunkReference("IHDR", 0)],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].Operation, Is.EqualTo("Remove"));
    Assert.That(result.Failures[0].Reason, Does.Contain("not removable"));
  }

  [Test]
  public void ApplyPlan_FuseNonFusible_Refused() {
    var data = _BuildPng(("tEXt", Encoding.ASCII.GetBytes("k\0v1")), ("tEXt", Encoding.ASCII.GetBytes("k\0v2")));
    var plan = new ChunkRewritePlan {
      Fuse = ["tEXt"],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].Operation, Is.EqualTo("Fuse"));
    Assert.That(result.Failures[0].Reason, Does.Contain("not fusible"));
  }

  [Test]
  public void ApplyPlan_TargetMissingChunk_Refused() {
    var data = _BuildPng();
    var plan = new ChunkRewritePlan {
      Placements = [new ChunkPlacementDirective(new ChunkReference("xYzQ", 0), ChunkZone.PreData)],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.False);
    Assert.That(result.Failures[0].Reason, Does.Contain("No chunk"));
  }

  [Test]
  public void ApplyPlan_AtomicFailure_FileNotMutated() {
    // A plan with one valid + one invalid directive must reject AS A WHOLE.
    var data = _BuildPng(("eXIf", new byte[] { 1 }), ("PLTE", new byte[3]));
    var plan = new ChunkRewritePlan {
      Placements = [
        new ChunkPlacementDirective(new ChunkReference("eXIf", 0), ChunkZone.PostData), // legal
        new ChunkPlacementDirective(new ChunkReference("PLTE", 0), ChunkZone.PostData), // illegal
      ],
    };

    var result = FormatRegistry.ApplyChunkPlan(data, plan);
    Assert.That(result.Success, Is.False);
    Assert.That(result.Bytes, Is.Null, "atomic — no partial rewrite when ANY directive fails");
    Assert.That(result.Failures, Has.Count.EqualTo(1));
    Assert.That(result.Failures[0].ChunkName, Is.EqualTo("PLTE"));
  }

  // ---- IDAT fuse (legal) ----

  [Test]
  public void ApplyPlan_FuseIdats_Succeeds() {
    using var ms = new MemoryStream();
    ms.Write(_PngSignature);
    _WriteChunk(ms, "IHDR", _Ihdr(1, 1));
    _WriteChunk(ms, "IDAT", new byte[] { 1, 2, 3 });
    _WriteChunk(ms, "IDAT", new byte[] { 4, 5, 6 });
    _WriteChunk(ms, "IDAT", new byte[] { 7, 8 });
    _WriteChunk(ms, "IEND", []);

    var data = ms.ToArray();
    var result = FormatRegistry.ApplyChunkPlan(data, new ChunkRewritePlan { Fuse = ["IDAT"] });
    Assert.That(result.Success, Is.True);

    var idats = FormatRegistry.EnumerateChunks(result.Bytes!).Where(c => c.Name == "IDAT").ToList();
    Assert.That(idats, Has.Count.EqualTo(1));
  }

  [Test]
  public void FormatEntry_Png_AdvertisesPlanRewrite() {
    var png = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(png, Is.Not.Null);
    Assert.That(png!.SupportsChunkPlanRewrite, Is.True);
  }
}
