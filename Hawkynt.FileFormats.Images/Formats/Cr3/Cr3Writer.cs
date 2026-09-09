using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Cr3;

/// <summary>Builds a CR3 container around a preview and an optional thumbnail.</summary>
/// <remarks>
/// The writer emits the ISO base-media and Canon boxes that carry pictures outside the camera's CRX
/// sensor track: the <c>ftyp</c> naming the <c>crx </c> brand, the Canon <c>uuid</c> in <c>moov</c>
/// carrying the codec string and optional thumbnail, and the preview's top-level <c>uuid</c>. It does
/// not invent sensor data, camera identity, lens information or exposure metadata.
///
/// <para>The container layout is the one used by the reader's ExifTool-checked fixture. In particular,
/// the preview JPEG starts forty-eight bytes into the preview UUID payload.</para>
/// </remarks>
public static class Cr3Writer {

  private const int _PreviewJpegOffset = 0x30;

  public static byte[] ToBytes(Cr3File file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PreviewJpeg is not { Length: > 0 } && file.ThumbnailJpeg is not { Length: > 0 })
      throw new ArgumentException("A CR3 written here needs a preview or a thumbnail to carry.", nameof(file));

    using var body = new MemoryStream();

    var version = file.CodecVersion is { Length: > 0 } ? file.CodecVersion : "CanonCR3_001/00.09.00/00.00.00";
    using var canon = new MemoryStream();
    canon.Write(_CanonUuid);
    _Box(canon, "CNCV", Encoding.ASCII.GetBytes(version));
    if (file.ThumbnailJpeg is { Length: > 0 } thumbnail) {
      var thumbnailWidth = _Dimension(file.ThumbnailWidth, nameof(Cr3File.ThumbnailWidth));
      var thumbnailHeight = _Dimension(file.ThumbnailHeight, nameof(Cr3File.ThumbnailHeight));

      using var thmb = new MemoryStream();
      thmb.Write(new byte[4]); // version and flags
      _UInt16(thmb, thumbnailWidth);
      _UInt16(thmb, thumbnailHeight);
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
      var previewWidth = _Dimension(file.PreviewWidth, nameof(Cr3File.PreviewWidth));
      var previewHeight = _Dimension(file.PreviewHeight, nameof(Cr3File.PreviewHeight));
      var payload = new byte[_PreviewJpegOffset + preview.Length];
      _PreviewUuid.CopyTo(payload.AsSpan());
      var header = payload.AsSpan(16);
      BinaryPrimitives.WriteUInt32BigEndian(header[4..], 1);
      BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)(8 + 24 + preview.Length));
      Encoding.ASCII.GetBytes("PRVW").CopyTo(header[12..]);
      BinaryPrimitives.WriteUInt16BigEndian(header[20..], 1);
      BinaryPrimitives.WriteUInt16BigEndian(header[22..], previewWidth);
      BinaryPrimitives.WriteUInt16BigEndian(header[24..], previewHeight);
      BinaryPrimitives.WriteUInt16BigEndian(header[26..], 1);
      BinaryPrimitives.WriteUInt32BigEndian(header[28..], (uint)preview.Length);
      preview.CopyTo(payload.AsSpan(_PreviewJpegOffset));
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

  private static ushort _Dimension(int value, string name)
    => value is > 0 and <= ushort.MaxValue
      ? (ushort)value
      : throw new ArgumentOutOfRangeException(name, value, $"CR3 dimensions must be between 1 and {ushort.MaxValue} pixels.");

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
