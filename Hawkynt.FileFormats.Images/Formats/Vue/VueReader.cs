using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Gif;

namespace FileFormat.Vue;

/// <summary>Reads Vue d'Esprit object files from bytes, streams, or file paths.</summary>
public static class VueReader {

  /// <summary>The name, a zero, and the version line that follows it.</summary>
  private const int _HEADER_SIZE = 30;

  /// <summary>Longer than any string either sample carries, and it keeps a misread length cheap.</summary>
  private const int _LARGEST_STRING = 4096;

  private static ReadOnlySpan<byte> _GifSignature => "GIF8"u8;

  public static VueFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vue object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VueFile FromStream(Stream stream) {
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

  public static VueFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static VueFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE || !data[..VueFile.Magic.Length].SequenceEqual(VueFile.Magic))
      throw new InvalidDataException("Not a Vue d'Esprit object: it does not open with the program's name.");

    var at = _HEADER_SIZE;
    var description = _ReadString(data, ref at);
    var name = _ReadString(data, ref at);
    if (at + 8 > data.Length)
      throw new InvalidDataException("Vue object ends before it states the size of its picture.");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
    at += 8;

    // Following the two lengths must land on the picture itself. Searching for the signature would
    // find one wherever it happened to be; arriving at it says the fields were read as meant.
    if (at + 6 > data.Length || !data.Slice(at, 4).SequenceEqual(_GifSignature))
      throw new InvalidDataException("Vue object does not carry its picture where its own fields say it does.");

    var embedded = data[at..].ToArray();
    var gif = GifReader.FromBytes(embedded);
    var stated = GifFile.ToRawImage(gif);
    if (stated.Width != width || stated.Height != height)
      throw new InvalidDataException($"Vue object states a picture of {width}x{height} and carries one of {stated.Width}x{stated.Height}.");

    return new() {
      Name = name,
      Description = description,
      Width = width,
      Height = height,
      Embedded = embedded,
    };
  }

  /// <summary>Reads one string: its length in two bytes, then that many characters.</summary>
  private static string _ReadString(ReadOnlySpan<byte> data, ref int at) {
    if (at + 2 > data.Length)
      throw new InvalidDataException("Vue object ends inside one of the strings it states.");

    var length = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    at += 2;
    if (length > _LARGEST_STRING || at + length > data.Length)
      throw new InvalidDataException("Vue object states a string longer than the file.");

    var text = Encoding.Latin1.GetString(data.Slice(at, length));
    at += length;
    return text;
  }
}
