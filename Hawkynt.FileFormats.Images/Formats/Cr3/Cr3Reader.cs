using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Cr3;

/// <summary>Reads a Canon CR3 and the pictures it carries beside its sensor data.</summary>
/// <remarks>
/// A CR3 is an ISO base media file — the same box structure as MP4 and HEIF —
/// with Canon's own boxes inside two <c>uuid</c> boxes. The sensor data is coded
/// with CRX, Canon's own wavelet codec, which is not implemented here and is
/// refused by name rather than guessed at.
///
/// <para>What is read is what the camera stored beside it: a full-size preview
/// and a thumbnail, both ordinary JPEGs. That is the same answer this package
/// already gives for every other raw format whose sensor compression it does not
/// know — the preview inside is a picture either way.</para>
///
/// <para>The two live in different places. The thumbnail is a <c>THMB</c> box in
/// the Canon <c>uuid</c> inside <c>moov</c>; the preview is a <c>PRVW</c> box in
/// a <c>uuid</c> of its own at the top level, whose JPEG begins at a fixed offset
/// of forty-eight bytes into that box's payload.</para>
/// </remarks>
public static class Cr3Reader {

  /// <summary>The brand a CR3 states, which is what tells it from any other ISO base media file.</summary>
  private static ReadOnlySpan<byte> _Brand => "crx "u8;

  /// <summary>The <c>uuid</c> inside <c>moov</c> that holds Canon's own boxes.</summary>
  private static ReadOnlySpan<byte> _CanonUuid => [
    0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48,
  ];

  /// <summary>The top-level <c>uuid</c> that holds the full-size preview.</summary>
  private static ReadOnlySpan<byte> _PreviewUuid => [
    0xEA, 0xF4, 0x2B, 0x5E, 0x1C, 0x98, 0x4B, 0x88, 0xB9, 0xFB, 0xB7, 0xDC, 0x40, 0x6E, 0x4D, 0x16,
  ];

  /// <summary>Where the preview's JPEG begins inside its <c>uuid</c> box's payload.</summary>
  private const int _PreviewJpegOffset = 0x30;

  public static Cr3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CR3 file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static Cr3File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromSpan(buffer.ToArray());
  }

  public static Cr3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static Cr3File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 16)
      throw new InvalidDataException("Data too small for a CR3.");

    var codecVersion = string.Empty;
    byte[]? preview = null;
    byte[]? thumbnail = null;
    var previewWidth = 0;
    var previewHeight = 0;
    var thumbnailWidth = 0;
    var thumbnailHeight = 0;
    var sawBrand = false;

    _Walk(data, 0, data.Length);
    if (!sawBrand)
      throw new InvalidDataException("Not a CR3: no ftyp box states the crx brand.");
    if (preview == null && thumbnail == null)
      throw new NotSupportedException(
        "This CR3 carries no preview or thumbnail, so the only picture in it is the sensor data, "
        + "which is coded with Canon's CRX and is not decoded here.");

    return new Cr3File {
      CodecVersion = codecVersion,
      PreviewJpeg = preview,
      PreviewWidth = previewWidth,
      PreviewHeight = previewHeight,
      ThumbnailJpeg = thumbnail,
      ThumbnailWidth = thumbnailWidth,
      ThumbnailHeight = thumbnailHeight,
    };

    void _Walk(ReadOnlySpan<byte> span, int at, int end) {
      while (at + 8 <= end) {
        long size = BinaryPrimitives.ReadUInt32BigEndian(span[at..]);
        var type = Encoding.ASCII.GetString(span.Slice(at + 4, 4));
        var header = 8;

        if (size == 1) {
          if (at + 16 > end)
            return;
          size = (long)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(at + 8, 8));
          header = 16;
        } else if (size == 0)
          size = end - at;

        if (size < header || at + size > end)
          return;

        var body = span.Slice(at + header, (int)size - header);
        switch (type) {
          case "ftyp":
            if (body.Length >= 4 && body[..4].SequenceEqual(_Brand))
              sawBrand = true;
            break;

          case "moov":
            _Walk(span, at + header, at + (int)size);
            break;

          case "uuid":
            if (body.Length < 16)
              break;
            if (body[..16].SequenceEqual(_CanonUuid))
              _Walk(span, at + header + 16, at + (int)size);
            else if (body[..16].SequenceEqual(_PreviewUuid))
              _ReadPreview(body);
            break;

          case "CNCV":
            codecVersion = Encoding.ASCII.GetString(body).TrimEnd('\0');
            break;

          case "THMB":
            _ReadThumbnail(body);
            break;
        }

        at += (int)size;
      }
    }

    void _ReadThumbnail(ReadOnlySpan<byte> body) {
      // A full box: version and flags, then width, height and the JPEG's length.
      const int fields = 4 + 2 + 2 + 4 + 4;
      if (body.Length < fields)
        return;

      var width = BinaryPrimitives.ReadUInt16BigEndian(body[4..]);
      var height = BinaryPrimitives.ReadUInt16BigEndian(body[6..]);
      var length = BinaryPrimitives.ReadUInt32BigEndian(body[8..]);
      if (length == 0 || fields + length > (uint)body.Length)
        return;

      thumbnail = body.Slice(fields, (int)length).ToArray();
      thumbnailWidth = width;
      thumbnailHeight = height;
    }

    void _ReadPreview(ReadOnlySpan<byte> body) {
      if (body.Length <= _PreviewJpegOffset)
        return;

      var width = BinaryPrimitives.ReadUInt16BigEndian(body[0x26..]);
      var height = BinaryPrimitives.ReadUInt16BigEndian(body[0x28..]);
      var length = BinaryPrimitives.ReadUInt32BigEndian(body[0x2C..]);
      var available = body.Length - _PreviewJpegOffset;
      if (length == 0 || length > (uint)available)
        length = (uint)available;

      preview = body.Slice(_PreviewJpegOffset, (int)length).ToArray();
      previewWidth = width;
      previewHeight = height;
    }
  }
}
