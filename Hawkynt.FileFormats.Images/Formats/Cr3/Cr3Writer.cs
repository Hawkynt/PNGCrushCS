using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Cr3;

/// <summary>Builds the CR3 container around a preview and a thumbnail.</summary>
/// <remarks>
/// This is not the registry's writer and deliberately not reachable as one: a
/// CR3 made from arbitrary pixels would claim to be a camera's file. What it is
/// for is checking the reader against something that is not this project —
/// a file built here is handed to ExifTool, which knows CR3 and reports what it
/// finds in it.
///
/// <para>What it writes is the part a reader needs: the <c>ftyp</c> that names
/// the brand, the Canon <c>uuid</c> in <c>moov</c> carrying the codec string and
/// the thumbnail, and the preview's own top-level <c>uuid</c>. No sensor data is
/// invented — the track structure a camera writes around CRX is simply absent.</para>
/// </remarks>
public static class Cr3Writer {

  private const int _PreviewJpegOffset = 0x30;

  public static byte[] ToBytes(Cr3File file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PreviewJpeg == null && file.ThumbnailJpeg == null)
      throw new ArgumentException("A CR3 written here needs a preview or a thumbnail to carry.", nameof(file));

    using var body = new MemoryStream();

    var version = file.CodecVersion is { Length: > 0 } ? file.CodecVersion : "CanonCR3_001/00.09.00/00.00.00";
    using var canon = new MemoryStream();
    canon.Write(_CanonUuid);
    _Box(canon, "CNCV", Encoding.ASCII.GetBytes(version));
    if (file.ThumbnailJpeg is { Length: > 0 } thumbnail) {
      using var thmb = new MemoryStream();
      thmb.Write(new byte[4]); // version and flags
      _UInt16(thmb, (ushort)file.ThumbnailWidth);
      _UInt16(thmb, (ushort)file.ThumbnailHeight);
      _UInt32(thmb, (uint)thumbnail.Length);
      thmb.Write(new byte[4]);
      thmb.Write(thumbnail);
      _Box(canon, "THMB", thmb.ToArray());
    }

    using var moov = new MemoryStream();
    var movieHeader = new byte[4 + 16 + 80];
    BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(12), 1000);
    _Box(moov, "mvhd", movieHeader);
    _Box(moov, "uuid", canon.ToArray());

    _Box(body, "ftyp", Encoding.ASCII.GetBytes("crx ").Concat4(1, "crx ", "isom"));
    _Box(body, "moov", moov.ToArray());

    if (file.PreviewJpeg is { Length: > 0 } preview) {
      var payload = new byte[16 + 32 + preview.Length];
      _PreviewUuid.CopyTo(payload.AsSpan());
      var header = payload.AsSpan(16);
      BinaryPrimitives.WriteUInt32BigEndian(header[4..], 1);
      BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)(8 + 24 + preview.Length));
      Encoding.ASCII.GetBytes("PRVW").CopyTo(header[12..]);
      BinaryPrimitives.WriteUInt16BigEndian(header[20..], 1);
      BinaryPrimitives.WriteUInt16BigEndian(header[22..], (ushort)file.PreviewWidth);
      BinaryPrimitives.WriteUInt16BigEndian(header[24..], (ushort)file.PreviewHeight);
      BinaryPrimitives.WriteUInt16BigEndian(header[26..], 1);
      BinaryPrimitives.WriteUInt32BigEndian(header[28..], (uint)preview.Length);
      preview.CopyTo(payload.AsSpan(16 + 32));
      _Box(body, "uuid", payload);
    }

    _Box(body, "mdat", new byte[16]);
    return body.ToArray();
  }

  private static ReadOnlySpan<byte> _CanonUuid => [
    0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48,
  ];

  private static ReadOnlySpan<byte> _PreviewUuid => [
    0xEA, 0xF4, 0x2B, 0x5E, 0x1C, 0x98, 0x4B, 0x88, 0xB9, 0xFB, 0xB7, 0xDC, 0x40, 0x6E, 0x4D, 0x16,
  ];

  private static void _Box(Stream target, string type, byte[] payload) {
    _UInt32(target, (uint)(8 + payload.Length));
    target.Write(Encoding.ASCII.GetBytes(type));
    target.Write(payload);
  }

  private static void _UInt32(Stream target, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    target.Write(bytes);
  }

  private static void _UInt16(Stream target, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    target.Write(bytes);
  }

  private static byte[] Concat4(this byte[] brand, uint minor, string a, string b) {
    var result = new byte[4 + 4 + 4 + 4];
    brand.AsSpan(0, 4).CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), minor);
    Encoding.ASCII.GetBytes(a).CopyTo(result.AsSpan(8));
    Encoding.ASCII.GetBytes(b).CopyTo(result.AsSpan(12));
    return result;
  }
}
