using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.EmbeddedPicture;

namespace FileFormat.Xar;

/// <summary>Walks a Xara drawing's records and reads the preview one of them holds.</summary>
public static class XarReader {

  /// <summary>Longer than any preview and short of what a record length typo would state.</summary>
  private const int _MaxPreviewLength = 16 * 1024 * 1024;

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
    var at = XarFile.Magic.Length;
    var first = true;

    while (at + XarFile.RecordHeaderSize <= data.Length) {
      var tag = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
      var body = at + XarFile.RecordHeaderSize;
      if (length > (uint)(data.Length - body))
        throw new InvalidDataException($"A Xara record at {at} states {length} bytes and the file has {data.Length - body} left.");

      var content = data.Slice(body, (int)length);

      // The format says the header comes first; a file whose first record is something else is
      // not being read the way it is laid out, and going on would be reading past a wrong guess.
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
          if (content.Length is < 1 or > _MaxPreviewLength)
            throw new InvalidDataException($"A Xara preview record states {content.Length} bytes.");

          return new() { Preview = EmbeddedPictureReader.Decode(content), PreviewTag = (int)tag, Producer = producer };

        case XarFile.TagStartCompression:
          throw new InvalidDataException("A Xara drawing carries no preview ahead of its compressed records.");
      }

      at = body + (int)length;
    }

    throw new InvalidDataException("A Xara drawing carries its picture in a preview record and this one has none.");
  }

  /// <summary>The producer name out of the file header, which is its fourth field.</summary>
  /// <remarks>
  /// Three bytes of file type, then three 32-bit numbers, then three strings each ended by a zero:
  /// the producer, its version and its build. Only the first is worth keeping and only as a label.
  /// </remarks>
  private static string? _ProducerOf(ReadOnlySpan<byte> header) {
    const int stringsBeginAt = 3 + 4 + 4 + 4;
    if (header.Length <= stringsBeginAt)
      return null;

    var rest = header[stringsBeginAt..];
    var end = rest.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? rest : rest[..end]);
  }
}
