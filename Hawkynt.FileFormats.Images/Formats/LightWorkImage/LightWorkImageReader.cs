using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.LightWorkImage;

/// <summary>Reads LightWork Design textures from bytes, streams, or file paths.</summary>
public static class LightWorkImageReader {

  /// <summary>More records ahead of the pixels than any of these has, and it stops a loop going nowhere.</summary>
  private const int _MaxLeadingRecords = 64;

  public static LightWorkImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LightWork image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LightWorkImageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static LightWorkImageFile FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;
    var copyright = _ReadString(data, ref at, LightWorkImageFile.TagCopyright);
    if (!copyright.StartsWith(LightWorkImageFile.Copyright, StringComparison.Ordinal))
      throw new InvalidDataException("Not a LightWork image: it does not open with the LightWorkImage copyright record.");

    // One bare word stands between the copyright and the first tagged record, with no tag of its own.
    _ReadWord(data, ref at);

    int width = 0, height = 0;
    string creator = string.Empty, author = string.Empty, source = string.Empty, date = string.Empty;
    var sized = false;

    for (var record = 0; ; ++record) {
      if (record > _MaxLeadingRecords)
        throw new InvalidDataException("A LightWork image states more records ahead of its pixels than one can have.");
      if (at >= data.Length)
        throw new InvalidDataException("A LightWork image ends before it states its picture.");

      var tag = data[at++];
      switch (tag) {
        case LightWorkImageFile.TagCreator: creator = _ReadStringBody(data, ref at); break;
        case LightWorkImageFile.TagAuthor: author = _ReadStringBody(data, ref at); break;
        case LightWorkImageFile.TagSource: source = _ReadStringBody(data, ref at); break;
        case LightWorkImageFile.TagDate: date = _ReadStringBody(data, ref at); break;
        case LightWorkImageFile.TagPicture:
          _ReadWord(data, ref at);
          break;
        case LightWorkImageFile.TagSize:
          width = _ReadWord(data, ref at);
          height = _ReadWord(data, ref at);
          sized = true;
          break;
        case LightWorkImageFile.TagWindow:
          _ReadWord(data, ref at);
          _ReadWord(data, ref at);
          _ReadWord(data, ref at);
          goto pixels;
        default:
          throw new InvalidDataException($"Unknown LightWork record 0x{tag:X2} at {at - 1}.");
      }
    }

  pixels:
    if (!sized)
      throw new InvalidDataException("A LightWork image states no size.");
    if (width < 1 || width > LightWorkImageFile.MaxDimension || height < 1 || height > LightWorkImageFile.MaxDimension)
      throw new InvalidDataException($"Invalid LightWork picture size {width}x{height}.");

    var needed = (long)width * height * 3;
    if (needed > int.MaxValue)
      throw new InvalidDataException($"A LightWork picture of {width}x{height} is larger than can be held.");

    var pixels = new byte[(int)needed];

    // Runs of a count and the colour it repeats. The count of what the runs cover has to come out at
    // the stated size to the pixel: short means the file was cut, long means it is not being read the
    // way it was written.
    var written = 0;
    while (written < pixels.Length) {
      if (at + 4 > data.Length)
        throw new InvalidDataException("A LightWork image ends in the middle of its pixels.");

      var count = data[at];
      if (count == 0)
        throw new InvalidDataException($"A LightWork run of no pixels at {at}.");

      var span = count * 3;
      if (written + span > pixels.Length)
        throw new InvalidDataException("A LightWork image states more pixels than its size allows.");

      var r = data[at + 1];
      var g = data[at + 2];
      var b = data[at + 3];
      for (var i = 0; i < count; ++i) {
        pixels[written] = r;
        pixels[written + 1] = g;
        pixels[written + 2] = b;
        written += 3;
      }

      at += 4;
    }

    // What is left has to be the rest of the record stream and has to stop at the last byte of the
    // file. That is what says the runs ended where the format meant them to rather than where a
    // wrong size happened to run out.
    _VerifyTail(data, at);

    return new() {
      Width = width,
      Height = height,
      Pixels = pixels,
      Creator = creator,
      Author = author,
      Source = source,
      Date = date,
    };
  }

  public static LightWorkImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Walks what follows the pixels; a bare word, then records ending at the last byte.</summary>
  private static void _VerifyTail(ReadOnlySpan<byte> data, int at) {
    _ReadWord(data, ref at);

    for (var record = 0; at < data.Length; ++record) {
      if (record > _MaxLeadingRecords)
        throw new InvalidDataException("A LightWork image carries more records after its pixels than one can have.");

      switch (data[at++]) {
        case LightWorkImageFile.TagCreator:
        case LightWorkImageFile.TagAuthor:
        case LightWorkImageFile.TagSource:
        case LightWorkImageFile.TagDate:
        case LightWorkImageFile.TagCopyright:
          _ReadStringBody(data, ref at);
          break;
        case LightWorkImageFile.TagPicture:
          _ReadWord(data, ref at);
          break;
        default:
          throw new InvalidDataException($"A LightWork image carries {data.Length - at + 1} bytes after its pixels that are not records.");
      }
    }
  }

  private static string _ReadString(ReadOnlySpan<byte> data, ref int at, byte expected) {
    if (at + 2 > data.Length || data[at] != expected)
      throw new InvalidDataException("Not a LightWork image: the first record is not the copyright string.");

    ++at;
    return _ReadStringBody(data, ref at);
  }

  private static string _ReadStringBody(ReadOnlySpan<byte> data, ref int at) {
    if (at >= data.Length)
      throw new InvalidDataException("A LightWork string record has no length.");

    var length = data[at++];
    if (at + length > data.Length)
      throw new InvalidDataException("A LightWork string record runs past the end of the file.");

    var text = Encoding.ASCII.GetString(data.Slice(at, length)).TrimEnd('\0');
    at += length;
    return text;
  }

  private static int _ReadWord(ReadOnlySpan<byte> data, ref int at) {
    if (at + 4 > data.Length)
      throw new InvalidDataException("A LightWork numeric record runs past the end of the file.");

    var value = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    at += 4;
    return value > int.MaxValue
      ? throw new InvalidDataException($"A LightWork record states {value}, which is not a size anything has.")
      : (int)value;
  }
}
