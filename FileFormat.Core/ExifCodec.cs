using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.Core;

/// <summary>
/// Reads and writes the TIFF byte stream that both PNG's <c>eXIf</c> chunk and JPEG's EXIF-flavoured
/// APP1 segment carry verbatim (JPEG additionally prefixes it with the 6-byte literal <c>"Exif\0\0"</c>,
/// which is the caller's concern, not this codec's — this class only ever sees/produces the TIFF bytes).
/// </summary>
/// <remarks>
/// <see cref="Write"/> is deliberately not a byte-exact inverse of what <see cref="TryParse"/> read: it
/// always emits little-endian, tags sorted ascending within each IFD (required by the TIFF spec, and
/// what every reader — including exiftool — expects), and packs overflow values back-to-back after the
/// IFD headers. That is the conventional shape every EXIF writer produces; byte-exact preservation of a
/// single file that never leaves its own format is handled separately, by each container's raw-chunk
/// passthrough (PNG's <c>ChunksBeforePlte</c>/etc., JPEG's <c>RawJpegBytes</c> lossless transcode).
/// <para/>
/// IFD1 (the thumbnail sub-IFD chain some encoders append after IFD0) is not preserved: we stop
/// following the "next IFD" pointer after IFD0's own sub-IFDs (Exif tag 0x8769, GPS tag 0x8825).
/// </remarks>
public static class ExifCodec {

  /// <summary>Byte length of one component of the given type. 0 for a type this codec has never heard
  /// of (which is not fatal — it only means we can't safely resolve inline-vs-offset storage for it).</summary>
  public static int TypeSize(ExifTagType type) => type switch {
    ExifTagType.Byte or ExifTagType.Ascii or ExifTagType.SByte or ExifTagType.Undefined => 1,
    ExifTagType.Short or ExifTagType.SShort => 2,
    ExifTagType.Long or ExifTagType.SLong or ExifTagType.Float => 4,
    ExifTagType.Rational or ExifTagType.SRational or ExifTagType.Double => 8,
    _ => 0,
  };

  /// <summary>Parses a TIFF byte stream (as embedded in PNG <c>eXIf</c> or JPEG APP1) into
  /// <see cref="ExifData"/>. Returns <c>null</c> on any structural problem — a bad or truncated blob is
  /// not something we can partially trust, so we refuse it wholesale rather than guess.</summary>
  public static ExifData? TryParse(ReadOnlySpan<byte> tiff) {
    if (tiff.Length < 8)
      return null;

    bool littleEndian;
    if (tiff[0] == 'I' && tiff[1] == 'I')
      littleEndian = true;
    else if (tiff[0] == 'M' && tiff[1] == 'M')
      littleEndian = false;
    else
      return null;

    var magic = _ReadU16(tiff, 2, littleEndian);
    if (magic != 42)
      return null;

    var ifd0Offset = (int)_ReadU32(tiff, 4, littleEndian);
    if (ifd0Offset <= 0 || ifd0Offset >= tiff.Length)
      return null;

    if (!_TryReadIfd(tiff, ifd0Offset, littleEndian, out var ifd0Entries, out _))
      return null;

    // Sub-IFD pointers live inside IFD0 but are meaningless once we've followed them — we always
    // regenerate them fresh on Write() rather than carry a stale offset forward.
    var kept = new List<ExifTagEntry>(ifd0Entries.Count);
    ExifIfd? exifIfd = null;
    ExifIfd? gpsIfd = null;

    foreach (var entry in ifd0Entries) {
      if (entry.Tag == ExifData.TagExifIfdPointer && entry.RawBytes.Length >= 4) {
        var off = (int)_RawToU32(entry.RawBytes, littleEndian);
        if (off > 0 && off < tiff.Length && _TryReadIfd(tiff, off, littleEndian, out var entries, out _))
          exifIfd = new ExifIfd { Entries = entries };
        continue;
      }

      if (entry.Tag == ExifData.TagGpsIfdPointer && entry.RawBytes.Length >= 4) {
        var off = (int)_RawToU32(entry.RawBytes, littleEndian);
        if (off > 0 && off < tiff.Length && _TryReadIfd(tiff, off, littleEndian, out var entries, out _))
          gpsIfd = new ExifIfd { Entries = entries };
        continue;
      }

      kept.Add(entry);
    }

    return new ExifData {
      LittleEndian = littleEndian,
      Ifd0 = new ExifIfd { Entries = kept },
      ExifIfd = exifIfd,
      GpsIfd = gpsIfd,
    };
  }

  /// <summary>Serializes <see cref="ExifData"/> back to a TIFF byte stream. See the type-level remarks
  /// for what is and isn't preserved byte-exactly.</summary>
  public static byte[] Write(ExifData data) {
    ArgumentNullException.ThrowIfNull(data);

    // Always little-endian on write — see remarks.
    const bool le = true;

    // IFD0 gets one extra pointer entry per sub-IFD that's present, sorted in with the rest.
    var ifd0Entries = data.Ifd0.Entries.ToList();
    if (data.ExifIfd != null)
      ifd0Entries.Add(new ExifTagEntry(ExifData.TagExifIfdPointer, ExifTagType.Long, 1, new byte[4]));
    if (data.GpsIfd != null)
      ifd0Entries.Add(new ExifTagEntry(ExifData.TagGpsIfdPointer, ExifTagType.Long, 1, new byte[4]));
    ifd0Entries = ifd0Entries.OrderBy(e => e.Tag).ToList();

    using var stream = new MemoryStream();
    // Header.
    stream.Write("II"u8);
    _WriteU16(stream, 42, le);
    _WriteU32(stream, 8, le);

    var ifd0Start = 8;
    var ifd0Size = 2 + 12 * ifd0Entries.Count + 4;
    var ifd0OverflowStart = ifd0Start + ifd0Size;

    // Sub-IFDs are laid out after IFD0's own overflow area. Pre-compute IFD0's overflow length so we
    // know where the Exif sub-IFD begins, then the GPS sub-IFD after that.
    var ifd0OverflowLen = _OverflowLength(ifd0Entries);
    var exifIfdStart = ifd0OverflowStart + ifd0OverflowLen;

    var exifEntries = data.ExifIfd?.Entries.OrderBy(e => e.Tag).ToList() ?? [];
    var exifIfdSize = data.ExifIfd != null ? 2 + 12 * exifEntries.Count + 4 : 0;
    var exifOverflowLen = data.ExifIfd != null ? _OverflowLength(exifEntries) : 0;
    var gpsIfdStart = exifIfdStart + exifIfdSize + exifOverflowLen;

    var gpsEntries = data.GpsIfd?.Entries.OrderBy(e => e.Tag).ToList() ?? [];

    // Patch the two pointer entries now that we know the real offsets.
    if (data.ExifIfd != null) {
      var idx = ifd0Entries.FindIndex(e => e.Tag == ExifData.TagExifIfdPointer);
      ifd0Entries[idx] = ifd0Entries[idx] with { RawBytes = _U32Bytes((uint)exifIfdStart, le) };
    }

    if (data.GpsIfd != null) {
      var idx = ifd0Entries.FindIndex(e => e.Tag == ExifData.TagGpsIfdPointer);
      ifd0Entries[idx] = ifd0Entries[idx] with { RawBytes = _U32Bytes((uint)gpsIfdStart, le) };
    }

    _WriteIfd(stream, ifd0Entries, ifd0OverflowStart, le, nextIfdOffset: 0);
    if (data.ExifIfd != null)
      _WriteIfd(stream, exifEntries, exifIfdStart + 2 + 12 * exifEntries.Count + 4, le, nextIfdOffset: 0);
    if (data.GpsIfd != null)
      _WriteIfd(stream, gpsEntries, gpsIfdStart + 2 + 12 * gpsEntries.Count + 4, le, nextIfdOffset: 0);

    return stream.ToArray();
  }

  // ---- reading ----

  private static bool _TryReadIfd(ReadOnlySpan<byte> tiff, int offset, bool le, out List<ExifTagEntry> entries, out int nextIfdOffset) {
    entries = [];
    nextIfdOffset = 0;
    if (offset + 2 > tiff.Length)
      return false;

    var count = _ReadU16(tiff, offset, le);
    var pos = offset + 2;
    if (pos + count * 12 + 4 > tiff.Length)
      return false;

    for (var i = 0; i < count; ++i) {
      var entryOffset = pos + i * 12;
      var tag = _ReadU16(tiff, entryOffset, le);
      var typeRaw = _ReadU16(tiff, entryOffset + 2, le);
      var compCount = (int)_ReadU32(tiff, entryOffset + 4, le);
      var type = (ExifTagType)typeRaw;
      var typeSize = TypeSize(type);

      if (typeSize == 0 || compCount < 0) {
        // Unknown type or nonsense count: keep the 4 raw value-field bytes verbatim as UNDEFINED
        // rather than guessing a layout we might get wrong.
        var fallback = tiff.Slice(entryOffset + 8, 4).ToArray();
        entries.Add(new ExifTagEntry(tag, ExifTagType.Undefined, 4, fallback));
        continue;
      }

      var byteLen = typeSize * compCount;
      byte[] raw;
      if (byteLen <= 4) {
        raw = tiff.Slice(entryOffset + 8, byteLen).ToArray();
      } else {
        var valueOffset = (int)_ReadU32(tiff, entryOffset + 8, le);
        if (valueOffset < 0 || valueOffset + byteLen > tiff.Length)
          continue; // corrupt offset — drop this one entry, keep the rest of the IFD.
        raw = tiff.Slice(valueOffset, byteLen).ToArray();
      }

      entries.Add(new ExifTagEntry(tag, type, compCount, raw));
    }

    nextIfdOffset = (int)_ReadU32(tiff, pos + count * 12, le);
    return true;
  }

  private static ushort _ReadU16(ReadOnlySpan<byte> data, int offset, bool le)
    => le ? (ushort)(data[offset] | (data[offset + 1] << 8)) : (ushort)((data[offset] << 8) | data[offset + 1]);

  private static uint _ReadU32(ReadOnlySpan<byte> data, int offset, bool le) => le
    ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
    : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

  private static uint _RawToU32(byte[] raw, bool le) => le
    ? (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24))
    : (uint)((raw[0] << 24) | (raw[1] << 16) | (raw[2] << 8) | raw[3]);

  // ---- writing ----

  private static int _OverflowLength(List<ExifTagEntry> entries) {
    var len = 0;
    foreach (var e in entries) {
      var byteLen = e.RawBytes.Length;
      if (byteLen <= 4)
        continue;
      len += byteLen;
      if ((len & 1) != 0)
        ++len; // word-align the next value, per TIFF convention.
    }

    return len;
  }

  private static void _WriteIfd(MemoryStream stream, List<ExifTagEntry> entries, int overflowStart, bool le, int nextIfdOffset) {
    _WriteU16(stream, (ushort)entries.Count, le);

    var overflowOffset = overflowStart;
    var overflowOffsets = new int[entries.Count];
    for (var i = 0; i < entries.Count; ++i) {
      var byteLen = entries[i].RawBytes.Length;
      if (byteLen <= 4)
        continue;
      overflowOffsets[i] = overflowOffset;
      overflowOffset += byteLen;
      if ((overflowOffset & 1) != 0)
        ++overflowOffset;
    }

    Span<byte> field = stackalloc byte[4];
    for (var i = 0; i < entries.Count; ++i) {
      var e = entries[i];
      _WriteU16(stream, e.Tag, le);
      _WriteU16(stream, (ushort)e.Type, le);
      _WriteU32(stream, (uint)e.Count, le);

      if (e.RawBytes.Length <= 4) {
        field.Clear();
        e.RawBytes.CopyTo(field);
        stream.Write(field);
      } else {
        _WriteU32(stream, (uint)overflowOffsets[i], le);
      }
    }

    _WriteU32(stream, (uint)nextIfdOffset, le);

    foreach (var e in entries) {
      if (e.RawBytes.Length <= 4)
        continue;
      stream.Write(e.RawBytes);
      if ((e.RawBytes.Length & 1) != 0)
        stream.WriteByte(0);
    }
  }

  private static void _WriteU16(Stream stream, ushort value, bool le) {
    if (le) { stream.WriteByte((byte)value); stream.WriteByte((byte)(value >> 8)); }
    else { stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
  }

  private static void _WriteU32(Stream stream, uint value, bool le) {
    if (le) {
      stream.WriteByte((byte)value); stream.WriteByte((byte)(value >> 8));
      stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 24));
    } else {
      stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
      stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
    }
  }

  private static byte[] _U32Bytes(uint value, bool le) {
    var b = new byte[4];
    if (le) { b[0] = (byte)value; b[1] = (byte)(value >> 8); b[2] = (byte)(value >> 16); b[3] = (byte)(value >> 24); }
    else { b[0] = (byte)(value >> 24); b[1] = (byte)(value >> 16); b[2] = (byte)(value >> 8); b[3] = (byte)value; }
    return b;
  }
}
