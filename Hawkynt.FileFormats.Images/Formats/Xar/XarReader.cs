using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;

namespace FileFormat.Xar;

/// <summary>Walks uncompressed Xara records, preferring a real bitmap object over a framework preview.</summary>
public static class XarReader {

  private const int _MaxEmbeddedLength = 64 * 1024 * 1024;
  private static ReadOnlySpan<byte> _PngMagic => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
  private static ReadOnlySpan<byte> _JpegMagic => [0xFF, 0xD8, 0xFF];

  public static XarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Xara drawing not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XarFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static XarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static XarFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < XarFile.Magic.Length + XarFile.RecordHeaderSize || !data[..XarFile.Magic.Length].SequenceEqual(XarFile.Magic))
      throw new InvalidDataException("Not a Xara drawing: it does not open with XARA.");

    string? producer = null;
    RawImage? preview = null;
    var previewTag = 0;
    RawImage? bitmap = null;
    var definitions = new Dictionary<uint, byte[]>();
    var at = XarFile.Magic.Length;
    var first = true;
    uint sequence = 1;

    while (at + XarFile.RecordHeaderSize <= data.Length) {
      var tag = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
      var body = at + XarFile.RecordHeaderSize;
      if (length > (uint)(data.Length - body))
        throw new InvalidDataException($"A Xara record at {at} states {length} bytes and the file has {data.Length - body} left.");

      var content = data.Slice(body, (int)length);

      if (first) {
        if (tag != XarFile.TagFileHeader)
          throw new InvalidDataException($"A Xara drawing opens with record {tag} rather than the file header.");

        producer = _ProducerOf(content);
        first = false;
      }

      switch (tag) {
        case XarFile.TagPreviewGif:
        case XarFile.TagPreviewJpeg:
        case XarFile.TagPreviewPng:
          if (content.Length is < 1 or > _MaxEmbeddedLength)
            throw new InvalidDataException($"A Xara preview record states {content.Length} bytes.");
          preview ??= EmbeddedPictureReader.Decode(content);
          if (previewTag == 0)
            previewTag = (int)tag;
          break;

        case XarFile.TagDefineBitmapJpeg:
        case XarFile.TagDefineBitmapPng:
        case XarFile.TagDefineBitmapPngReal: {
          var payload = _BitmapPayload(content, tag);
          if (payload.Length > 0)
            definitions[sequence] = payload;
          break;
        }

        case XarFile.TagNodeBitmap:
          if (content.Length != 36)
            throw new InvalidDataException($"A Xara bitmap object is {content.Length} bytes where the specification defines 36.");
          var reference = BinaryPrimitives.ReadUInt32LittleEndian(content[32..]);
          if (definitions.TryGetValue(reference, out var definition))
            bitmap = EmbeddedPictureReader.Decode(definition);
          break;

        case XarFile.TagStartCompression:
          // A complete XAR reader would inflate the following record stream. For this image-format
          // reader an already resolved document bitmap is better than the preview, and a preview is
          // still a truthful fallback when all real drawing records are compressed.
          if (bitmap != null || preview != null)
            return new() { Bitmap = bitmap, Preview = preview, PreviewTag = previewTag, Producer = producer };
          throw new InvalidDataException("A Xara drawing reaches its compressed record stream before any bitmap object or preview this reader can draw.");

        case XarFile.TagEndOfFile:
          if (bitmap != null || preview != null)
            return new() { Bitmap = bitmap, Preview = preview, PreviewTag = previewTag, Producer = producer };
          throw new InvalidDataException("A Xara drawing reaches end-of-file without a bitmap object or preview this reader can draw.");
      }

      at = body + (int)length;
      ++sequence;
    }

    if (bitmap != null || preview != null)
      return new() { Bitmap = bitmap, Preview = preview, PreviewTag = previewTag, Producer = producer };

    throw new InvalidDataException("A Xara drawing carries no bitmap object or preview this reader can draw.");
  }

  private static byte[] _BitmapPayload(ReadOnlySpan<byte> content, uint tag) {
    var signature = tag == XarFile.TagDefineBitmapJpeg ? _JpegMagic : _PngMagic;
    var offset = content.IndexOf(signature);
    if (offset < 0)
      throw new InvalidDataException($"Xara bitmap-definition record {tag} carries no {(tag == XarFile.TagDefineBitmapJpeg ? "JPEG" : "PNG")} after its name.");

    var payload = content[offset..];
    if (payload.Length > _MaxEmbeddedLength)
      throw new InvalidDataException($"A Xara bitmap definition states {payload.Length} embedded bytes.");
    return payload.ToArray();
  }

  /// <summary>The producer name out of the file header, which is its fourth field.</summary>
  private static string? _ProducerOf(ReadOnlySpan<byte> header) {
    const int stringsBeginAt = 3 + 4 + 4 + 4;
    if (header.Length <= stringsBeginAt)
      return null;

    var rest = header[stringsBeginAt..];
    var end = rest.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? rest : rest[..end]);
  }
}
