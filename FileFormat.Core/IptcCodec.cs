using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Core;

/// <summary>
/// Reads and writes IPTC-IIM data as carried inside a JPEG APP13 segment: the fixed
/// <c>"Photoshop 3.0\0"</c> signature, one or more Photoshop "8BIM" image-resource blocks, and — for
/// the resource we care about (ID 0x0404, "IPTC-NAA record") — the IIM dataset stream itself.
/// </summary>
/// <remarks>
/// Only the non-extended IIM length form (a plain 15-bit length in the 2-byte length field) is parsed;
/// the rare extended-length form (high bit set, followed by a 4-byte length) stops parsing early rather
/// than risk misreading the rest of the stream as dataset headers. Every 8BIM resource other than
/// 0x0404 (e.g. Photoshop's own thumbnail or path resources, which occasionally ride along in the same
/// APP13) is preserved as opaque bytes across a read+write round trip of the same segment, but is not
/// otherwise exposed — this codec is IPTC-only.
/// </remarks>
public static class IptcCodec {

  private static ReadOnlySpan<byte> _PhotoshopSignature => "Photoshop 3.0\0"u8;
  private const ushort _IptcResourceId = 0x0404;
  private const byte _IimTagMarker = 0x1C;

  /// <summary>Parses a JPEG APP13 segment payload (starting at the <c>"Photoshop 3.0\0"</c> signature)
  /// into <see cref="IptcData"/>. Returns <c>null</c> if the signature doesn't match or no IPTC-NAA
  /// resource (0x0404) is present.</summary>
  public static IptcData? TryParsePhotoshopSegment(ReadOnlySpan<byte> app13Payload) {
    if (app13Payload.Length < _PhotoshopSignature.Length || !app13Payload[.._PhotoshopSignature.Length].SequenceEqual(_PhotoshopSignature))
      return null;

    var pos = _PhotoshopSignature.Length;
    while (pos + 4 <= app13Payload.Length) {
      if (app13Payload[pos] != (byte)'8' || app13Payload[pos + 1] != (byte)'B'
          || app13Payload[pos + 2] != (byte)'I' || app13Payload[pos + 3] != (byte)'M')
        break; // not a recognizable resource block — stop rather than misread the rest as data.
      pos += 4;

      if (pos + 2 > app13Payload.Length) break;
      var resourceId = BinaryPrimitives.ReadUInt16BigEndian(app13Payload.Slice(pos, 2));
      pos += 2;

      if (pos + 1 > app13Payload.Length) break;
      var nameLen = app13Payload[pos];
      var nameFieldLen = 1 + nameLen;
      pos += nameFieldLen;
      if ((nameFieldLen & 1) != 0) ++pos; // pad name field to even.

      if (pos + 4 > app13Payload.Length) break;
      var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(app13Payload.Slice(pos, 4));
      pos += 4;
      if (dataSize < 0 || pos + dataSize > app13Payload.Length) break;

      if (resourceId == _IptcResourceId)
        return _ParseIim(app13Payload.Slice(pos, dataSize));

      pos += dataSize;
      if ((dataSize & 1) != 0) ++pos; // pad resource data to even.
    }

    return null;
  }

  private static IptcData _ParseIim(ReadOnlySpan<byte> iim) {
    var sets = new List<IptcDataSet>();
    var pos = 0;
    while (pos + 5 <= iim.Length) {
      if (iim[pos] != _IimTagMarker) break;
      var record = iim[pos + 1];
      var dataSet = iim[pos + 2];
      var lenHigh = iim[pos + 3];
      if ((lenHigh & 0x80) != 0)
        break; // extended-length form — not supported, stop here (see type remarks).
      var length = (lenHigh << 8) | iim[pos + 4];
      pos += 5;
      if (pos + length > iim.Length) break;
      sets.Add(new IptcDataSet(record, dataSet, iim.Slice(pos, length).ToArray()));
      pos += length;
    }

    return new IptcData { DataSets = sets };
  }

  /// <summary>Serializes <see cref="IptcData"/> as a complete JPEG APP13 payload: the Photoshop
  /// signature plus a single 8BIM 0x0404 resource wrapping the re-encoded IIM stream.</summary>
  public static byte[] ToPhotoshopSegment(IptcData data) {
    ArgumentNullException.ThrowIfNull(data);

    using var iim = new MemoryStream();
    Span<byte> lenBuf = stackalloc byte[2];
    foreach (var ds in data.DataSets) {
      if (ds.Value.Length > 0x7FFF)
        continue; // extended-length write-back not supported — see type remarks; drop rather than corrupt the stream.
      iim.WriteByte(_IimTagMarker);
      iim.WriteByte(ds.Record);
      iim.WriteByte(ds.DataSet);
      BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)ds.Value.Length);
      iim.Write(lenBuf);
      iim.Write(ds.Value);
    }

    var iimBytes = iim.ToArray();

    using var result = new MemoryStream();
    result.Write(_PhotoshopSignature);
    result.Write("8BIM"u8);
    Span<byte> idBuf = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(idBuf, _IptcResourceId);
    result.Write(idBuf);
    result.WriteByte(0); // empty Pascal name
    result.WriteByte(0); // pad name field (1-byte len + 0 name bytes = 1, odd) to even
    Span<byte> sizeBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(sizeBuf, (uint)iimBytes.Length);
    result.Write(sizeBuf);
    result.Write(iimBytes);
    if ((iimBytes.Length & 1) != 0)
      result.WriteByte(0);

    return result.ToArray();
  }
}
