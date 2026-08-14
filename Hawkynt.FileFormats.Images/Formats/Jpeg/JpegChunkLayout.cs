using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>
/// JPEG byte-level layout + by-name + plan-based rewrite. Implements <see cref="IFormatChunkLayout{TSelf}"/>,
/// <see cref="IFormatChunkRewriter{TSelf}"/>, and <see cref="IFormatChunkPlanRewriter{TSelf}"/> for
/// <see cref="JpegFile"/>.
/// </summary>
/// <remarks>
/// Each segment becomes one <see cref="ChunkSpan"/> named by its short marker mnemonic (SOI, APP1,
/// COM, DQT, etc.). The entropy-coded image data following SOS is treated as a single locked span
/// named "ECS" (Entropy-Coded Segment).
/// <para/>
/// Zone partitioning:
/// <list type="bullet">
///   <item>Signature: SOI</item>
///   <item>PreData: APP*, COM appearing before any SOF / DQT / DHT / DRI / SOS</item>
///   <item>Data: SOF, DQT, DHT, DRI, SOS, ECS, restart markers — the image-encoding pipeline</item>
///   <item>PostData: APP*, COM appearing after EOI</item>
///   <item>Footer: EOI</item>
/// </list>
/// Only APP markers and COM are movable/removable; everything else is Fixed because changing it
/// would break the encoded image. Fuse is not supported on JPEG (no fuse-equivalent semantics).
/// </remarks>
internal static class JpegChunkLayout {

  private const string _Signature = "SOI";
  private const string _Footer = "EOI";
  private const string _EntropySegment = "ECS";

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < 2 || data[0] != 0xFF || data[1] != JpegMarker.SOI) return result;

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
    int NextOrdinal(string name) {
      var n = ordinals.TryGetValue(name, out var v) ? v : 0;
      ordinals[name] = n + 1;
      return n;
    }

    // SOI is always 2 bytes at offset 0.
    result.Add(new ChunkSpan(_Signature, 0, 2, ChunkKind.Signature, ChunkMobility.Fixed,
      Ordinal: NextOrdinal(_Signature), CurrentZone: ChunkZone.Signature, AllowedZones: AllowedZones.Signature));

    // First pass: parse raw segments; second pass assigns zones once we know where SOS/EOI are.
    var raw = new List<(string Name, int Offset, int Length, ChunkKind Kind, ChunkMobility Mobility)>();
    var pos = 2;
    var firstFrameDataIdx = -1; // index in raw where Data zone starts

    while (pos + 1 < data.Length) {
      // Skip filler 0xFF bytes between segments (legal per spec).
      while (pos < data.Length - 1 && data[pos] == 0xFF && data[pos + 1] == 0xFF) pos++;
      if (pos + 1 >= data.Length) break;
      if (data[pos] != 0xFF) break;
      var marker = data[pos + 1];

      if (marker == JpegMarker.EOI) {
        result.Add(new ChunkSpan(_Footer, pos, 2, ChunkKind.Footer, ChunkMobility.Fixed,
          Ordinal: NextOrdinal(_Footer), CurrentZone: ChunkZone.Footer, AllowedZones: AllowedZones.Footer));
        pos += 2;
        break;
      }

      // Markers without payload (RST0..RST7, TEM=0x01) — 2 bytes total.
      if (JpegMarker.IsRst(marker) || marker == 0x01) {
        var emptyName = JpegMarker.IsRst(marker) ? $"RST{marker - JpegMarker.RST0}" : "TEM";
        raw.Add((emptyName, pos, 2, ChunkKind.Unknown, ChunkMobility.Fixed));
        if (firstFrameDataIdx < 0) firstFrameDataIdx = raw.Count - 1;
        pos += 2;
        continue;
      }

      // Length-prefixed segment.
      if (pos + 4 > data.Length) break;
      var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 2, 2));
      if (segLen < 2 || pos + 2 + segLen > data.Length) break;
      var totalSegLen = 2 + segLen; // marker prefix (FF + marker byte) + length-prefixed body
      var segName = _MarkerName(marker);
      var (kind, mobility, isDataZone) = _Classify(marker);
      raw.Add((segName, pos, totalSegLen, kind, mobility));
      if (isDataZone && firstFrameDataIdx < 0) firstFrameDataIdx = raw.Count - 1;

      pos += totalSegLen;

      // SOS is followed by entropy-coded data until the next non-restart marker.
      if (marker == JpegMarker.SOS) {
        var ecsStart = pos;
        var ecsEnd = _ScanEntropyData(data, pos);
        if (ecsEnd > ecsStart) {
          raw.Add((_EntropySegment, ecsStart, ecsEnd - ecsStart, ChunkKind.PixelData, ChunkMobility.Fixed));
        }
        pos = ecsEnd;
      }
    }

    // Anything after EOI is PostData (e.g. trailing XMP, EXIF, JPS).
    while (pos + 1 < data.Length) {
      if (data[pos] != 0xFF) { pos++; continue; }
      var trailMarker = data[pos + 1];
      if (trailMarker == JpegMarker.SOI || trailMarker == JpegMarker.EOI || JpegMarker.IsRst(trailMarker) || trailMarker == 0x01) { pos += 2; continue; }
      if (pos + 4 > data.Length) break;
      var trailLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 2, 2));
      if (trailLen < 2 || pos + 2 + trailLen > data.Length) break;
      var trailName = _MarkerName(trailMarker);
      var (trailKind, trailMobility, _) = _Classify(trailMarker);
      raw.Add((trailName, pos, 2 + trailLen, trailKind, trailMobility));
      pos += 2 + trailLen;
    }

    // Zone assignment.
    for (var i = 0; i < raw.Count; ++i) {
      var r = raw[i];
      var zone = r.Name switch {
        _Footer => ChunkZone.Footer,
        _EntropySegment => ChunkZone.Data,
        _ when firstFrameDataIdx >= 0 && i < firstFrameDataIdx => ChunkZone.PreData,
        _ when firstFrameDataIdx >= 0 && i >= firstFrameDataIdx => _IsDataZoneSegmentByName(r.Name) ? ChunkZone.Data : _IsAfterEoi(raw, i) ? ChunkZone.PostData : ChunkZone.PreData,
        _ => ChunkZone.PreData,
      };
      var allowed = _AllowedZonesForChunk(r.Name, r.Mobility);
      result.Add(new ChunkSpan(r.Name, r.Offset, r.Length, r.Kind, r.Mobility, NextOrdinal(r.Name), zone, allowed));
    }

    return result;
  }

  private static bool _IsDataZoneSegmentByName(string name)
    => name is "SOF" or "DQT" or "DHT" or "DRI" or "SOS" or "DAC" or _EntropySegment
       || name.StartsWith("RST", StringComparison.Ordinal);

  private static bool _IsAfterEoi(List<(string Name, int Offset, int Length, ChunkKind Kind, ChunkMobility Mobility)> raw, int idx) {
    for (var j = 0; j < idx; ++j) if (raw[j].Name == _Footer) return true;
    return false;
  }

  private static AllowedZones _AllowedZonesForChunk(string name, ChunkMobility mobility) {
    if ((mobility & ChunkMobility.Movable) == 0)
      return _IsDataZoneSegmentByName(name) ? AllowedZones.Data
            : name == _Signature ? AllowedZones.Signature
            : name == _Footer ? AllowedZones.Footer
            : AllowedZones.PreData;
    // Movable markers (APP*, COM): either side of the data block.
    return AllowedZones.PreData | AllowedZones.PostData;
  }

  private static string _MarkerName(byte marker) {
    if (JpegMarker.IsApp(marker)) return $"APP{marker - JpegMarker.APP0}";
    if (JpegMarker.IsRst(marker)) return $"RST{marker - JpegMarker.RST0}";
    if (marker is >= JpegMarker.SOF0 and <= 0xCF && marker != JpegMarker.DHT && marker != 0xC8 && marker != 0xCC)
      return $"SOF{marker - JpegMarker.SOF0}";
    return marker switch {
      JpegMarker.DHT => "DHT",
      JpegMarker.DQT => "DQT",
      JpegMarker.DRI => "DRI",
      JpegMarker.SOS => "SOS",
      JpegMarker.COM => "COM",
      0xC8 => "JPG",
      0xCC => "DAC",
      _ => $"M{marker:X2}",
    };
  }

  private static (ChunkKind Kind, ChunkMobility Mobility, bool IsDataZone) _Classify(byte marker) {
    if (JpegMarker.IsApp(marker)) return (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, false);
    if (marker == JpegMarker.COM) return (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, false);
    if (JpegMarker.IsRst(marker)) return (ChunkKind.PixelData, ChunkMobility.Fixed, true);
    if (marker is >= JpegMarker.SOF0 and <= 0xCF && marker != JpegMarker.DHT && marker != 0xC8 && marker != 0xCC)
      return (ChunkKind.Header, ChunkMobility.Fixed, true);
    return marker switch {
      JpegMarker.DHT or JpegMarker.DQT or JpegMarker.DRI or JpegMarker.SOS or 0xC8 or 0xCC
        => (ChunkKind.Header, ChunkMobility.Fixed, true),
      _ => (ChunkKind.Unknown, ChunkMobility.Fixed, false),
    };
  }

  /// <summary>Scans entropy-coded data starting at <paramref name="pos"/>, returning the offset of the
  /// next genuine marker (0xFF followed by a non-zero, non-RST byte) or end of file.</summary>
  /// <summary>
  /// The length of the JPEG beginning at offset zero, or 0 if it has no end-of-image marker.
  /// </summary>
  /// <remarks>
  /// For splitting a stream of JPEGs laid end to end — a Motion JPEG file, or the frame chunks of an
  /// MJPG AVI. <see cref="Enumerate"/> answers this too, in the offset of its footer, but it goes on
  /// to walk everything after the marker as trailing metadata: called once per frame on the rest of
  /// the stream, that is quadratic in the file's length, and a stream is exactly where a file is long
  /// and the frames are many. This walks one picture and stops.
  /// <para/>
  /// Searching for <c>FF D9</c> instead would be linear too and wrong: entropy-coded data may contain
  /// those two bytes, and a frame carrying a thumbnail has a whole second JPEG inside its APP1 with
  /// an end marker of its own. Hence the same segment walk and the same <see cref="_ScanEntropyData"/>
  /// as above — the delicate part stays in one place.
  /// </remarks>
  internal static int FirstImageLength(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0xFF || data[1] != JpegMarker.SOI)
      return 0;

    var pos = 2;
    while (pos + 1 < data.Length) {
      // Filler bytes between segments are legal.
      while (pos < data.Length - 1 && data[pos] == 0xFF && data[pos + 1] == 0xFF)
        ++pos;
      if (pos + 1 >= data.Length || data[pos] != 0xFF)
        return 0;

      var marker = data[pos + 1];
      if (marker == JpegMarker.EOI)
        return pos + 2;

      // Markers standing alone, carrying no length.
      if (JpegMarker.IsRst(marker) || marker == 0x01 || marker == JpegMarker.SOI) {
        pos += 2;
        continue;
      }

      if (pos + 4 > data.Length)
        return 0;

      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 2, 2));
      if (segmentLength < 2 || pos + 2 + segmentLength > data.Length)
        return 0;

      pos += 2 + segmentLength;
      if (marker == JpegMarker.SOS)
        pos = _ScanEntropyData(data, pos);
    }

    return 0;
  }

  private static int _ScanEntropyData(ReadOnlySpan<byte> data, int pos) {
    while (pos < data.Length - 1) {
      if (data[pos] != 0xFF) { pos++; continue; }
      var next = data[pos + 1];
      if (next == 0x00) { pos += 2; continue; }                       // stuffed byte — part of entropy data
      if (next >= JpegMarker.RST0 && next <= JpegMarker.RST7) { pos += 2; continue; } // restart marker, still in scan
      return pos;
    }
    return data.Length;
  }

  // ---- by-name rewrite (lenient) ----

  public static byte[] Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules) {
    var chunks = Enumerate(data);
    if (chunks.Count == 0) return data.ToArray();

    var preDataMovable = new List<ChunkSpan>();
    var postDataMovable = new List<ChunkSpan>();
    var dataZone = new List<ChunkSpan>(); // includes ECS + frame markers in original order
    ChunkSpan? signature = null;
    ChunkSpan? footer = null;

    foreach (var ch in chunks) {
      if (ch.Name == _Signature) { signature = ch; continue; }
      if (ch.Name == _Footer) { footer = ch; continue; }
      switch (ch.CurrentZone) {
        case ChunkZone.PreData: preDataMovable.Add(ch); break;
        case ChunkZone.PostData: postDataMovable.Add(ch); break;
        default: dataZone.Add(ch); break;
      }
    }

    foreach (var rule in rules) {
      switch (rule.Placement) {
        case ChunkPlacement.Remove:
          _RemoveAllNamed(preDataMovable, rule.Name);
          _RemoveAllNamed(postDataMovable, rule.Name);
          break;
        case ChunkPlacement.BeforeData:
          _MoveAllNamed(postDataMovable, preDataMovable, rule.Name);
          break;
        case ChunkPlacement.AfterData:
          _MoveAllNamed(preDataMovable, postDataMovable, rule.Name);
          break;
      }
    }

    using var output = new MemoryStream(data.Length);
    if (signature is { } s) _CopyChunkBytes(data, s, output);
    foreach (var ch in preDataMovable) _CopyChunkBytes(data, ch, output);
    foreach (var ch in dataZone) _CopyChunkBytes(data, ch, output);
    if (footer is { } f) _CopyChunkBytes(data, f, output);
    foreach (var ch in postDataMovable) _CopyChunkBytes(data, ch, output);
    return output.ToArray();
  }

  // ---- plan-based rewrite (strict) ----

  public static ChunkRewriteResult ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan) {
    var failures = new List<ChunkRewriteFailure>();
    var chunks = Enumerate(data);
    if (chunks.Count == 0) {
      failures.Add(new ChunkRewriteFailure("Validate", "(file)", 0, "Not a valid JPEG."));
      return new ChunkRewriteResult { Failures = failures };
    }

    var byRef = new Dictionary<ChunkReference, ChunkSpan>();
    foreach (var ch in chunks)
      byRef[new ChunkReference(ch.Name, ch.Ordinal)] = ch;

    var requestedZone = new Dictionary<ChunkReference, (ChunkZone Zone, int? Order)>();
    foreach (var p in plan.Placements) {
      if (!byRef.TryGetValue(p.Chunk, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"No chunk with name '{p.Chunk.Name}' and ordinal {p.Chunk.Ordinal} found."));
        continue;
      }
      var asFlag = _ZoneToFlag(p.TargetZone);
      if ((ch.AllowedZones & asFlag) == 0) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"Chunk '{p.Chunk.Name}' may not occupy zone {p.TargetZone}. Allowed: {ch.AllowedZones}."));
        continue;
      }
      requestedZone[p.Chunk] = (p.TargetZone, p.OrderInZone);
    }

    var toRemove = new HashSet<ChunkReference>();
    foreach (var r in plan.Remove) {
      if (!byRef.TryGetValue(r, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal, $"No chunk found."));
        continue;
      }
      if ((ch.Mobility & ChunkMobility.Removable) == 0) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal,
          $"Chunk '{r.Name}' is not removable (mobility: {ch.Mobility})."));
        continue;
      }
      toRemove.Add(r);
    }

    foreach (var name in plan.Fuse)
      failures.Add(new ChunkRewriteFailure("Fuse", name, 0, "JPEG does not support chunk fusion."));

    if (failures.Count > 0)
      return new ChunkRewriteResult { Failures = failures };

    var preData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var postData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var dataZone = new List<ChunkSpan>();
    ChunkSpan? signature = null;
    ChunkSpan? footer = null;

    for (var i = 0; i < chunks.Count; ++i) {
      var ch = chunks[i];
      if (ch.Name == _Signature) { signature = ch; continue; }
      if (ch.Name == _Footer) { footer = ch; continue; }
      if (toRemove.Contains(new ChunkReference(ch.Name, ch.Ordinal))) continue;
      var key = new ChunkReference(ch.Name, ch.Ordinal);
      var (zone, order) = requestedZone.TryGetValue(key, out var pz) ? pz : (ch.CurrentZone, (int?)null);
      switch (zone) {
        case ChunkZone.PreData: preData.Add((ch, order, i)); break;
        case ChunkZone.PostData: postData.Add((ch, order, i)); break;
        default: dataZone.Add(ch); break;
      }
    }

    if (signature == null) {
      failures.Add(new ChunkRewriteFailure("Validate", "SOI", 0, "SOI missing after applying plan."));
      return new ChunkRewriteResult { Failures = failures };
    }

    static int Compare((ChunkSpan Ch, int? Order, int OriginalIdx) a, (ChunkSpan Ch, int? Order, int OriginalIdx) b) {
      if (a.Order.HasValue && b.Order.HasValue) return a.Order.Value.CompareTo(b.Order.Value);
      if (a.Order.HasValue) return -1;
      if (b.Order.HasValue) return 1;
      return a.OriginalIdx.CompareTo(b.OriginalIdx);
    }
    preData.Sort(Compare);
    postData.Sort(Compare);

    using var output = new MemoryStream(data.Length);
    _CopyChunkBytes(data, signature.Value, output);
    foreach (var (ch, _, _) in preData) _CopyChunkBytes(data, ch, output);
    foreach (var ch in dataZone) _CopyChunkBytes(data, ch, output);
    if (footer is { } f) _CopyChunkBytes(data, f, output);
    foreach (var (ch, _, _) in postData) _CopyChunkBytes(data, ch, output);
    return new ChunkRewriteResult { Bytes = output.ToArray() };
  }

  // ---- helpers ----

  private static void _RemoveAllNamed(List<ChunkSpan> bucket, string name) {
    for (var i = bucket.Count - 1; i >= 0; --i) {
      var ch = bucket[i];
      if (ch.Name != name) continue;
      if ((ch.Mobility & ChunkMobility.Removable) == 0) continue;
      bucket.RemoveAt(i);
    }
  }

  private static void _MoveAllNamed(List<ChunkSpan> source, List<ChunkSpan> dest, string name) {
    for (var i = source.Count - 1; i >= 0; --i) {
      var ch = source[i];
      if (ch.Name != name) continue;
      if ((ch.Mobility & ChunkMobility.Movable) == 0) continue;
      source.RemoveAt(i);
      dest.Add(ch);
    }
  }

  private static void _CopyChunkBytes(ReadOnlySpan<byte> data, ChunkSpan ch, MemoryStream output) {
    var slice = data.Slice((int)ch.Offset, (int)ch.Length);
    output.Write(slice);
  }

  private static AllowedZones _ZoneToFlag(ChunkZone zone) => zone switch {
    ChunkZone.Signature => AllowedZones.Signature,
    ChunkZone.Header => AllowedZones.Header,
    ChunkZone.PreData => AllowedZones.PreData,
    ChunkZone.Data => AllowedZones.Data,
    ChunkZone.PostData => AllowedZones.PostData,
    ChunkZone.Footer => AllowedZones.Footer,
    _ => AllowedZones.None,
  };
}
