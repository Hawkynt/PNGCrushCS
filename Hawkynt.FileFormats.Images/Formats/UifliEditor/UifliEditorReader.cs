using System;
using System.IO;

namespace FileFormat.UifliEditor;

/// <summary>Reads UIFLI-editor pictures from bytes, streams, or file paths.</summary>
public static class UifliEditorReader {

  public static UifliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UifliEditorFile FromStream(Stream stream) {
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

  public static UifliEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 6)
      throw new InvalidDataException($"Not a UIFLI picture: {data.Length} bytes.");

    return new() { Data = _Unpack(data) };
  }

  /// <summary>
  /// Unpacks the run-length encoding, which runs backwards: the last byte of the file is the first
  /// one read, and the picture fills from its end towards its start.
  /// </summary>
  /// <remarks>
  /// Unpacking backwards lets the packed data sit immediately after the loader that reads it, with
  /// the two growing towards each other — so a picture that packs well needs no gap between them
  /// and the whole thing loads as one block. Within a command the bytes are in the order the
  /// backwards reader meets them, which is the reverse of how a forward packer would write them.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var escape = data[2];
    var unpacked = new byte[UifliEditorFile.UnpackedSize];
    var at = data.Length;

    for (var target = UifliEditorFile.UnpackedSize - 1; target >= 0;) {
      var value = _Previous(data, ref at);
      var count = 1;

      if (value == escape) {
        count = _Previous(data, ref at);
        if (count == 0)
          count = 256;

        value = _Previous(data, ref at);
      }

      while (count-- > 0 && target >= 0)
        unpacked[target--] = value;
    }

    return unpacked;
  }

  /// <summary>Reads the byte before the one last read; the first three are never data.</summary>
  private static byte _Previous(ReadOnlySpan<byte> data, ref int at) {
    if (at <= 2)
      throw new InvalidDataException("A UIFLI picture ends before its picture does.");

    return data[--at];
  }

  public static UifliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
