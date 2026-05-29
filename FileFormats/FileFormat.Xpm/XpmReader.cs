using System;
using System.IO;
using System.Text;

namespace FileFormat.Xpm;

/// <summary>Reads XPM files from bytes, streams, or file paths.</summary>
public static class XpmReader {

  private static readonly byte[] _Magic = "/* XPM */"u8.ToArray();

  public static XpmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XPM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XpmFile FromStream(Stream stream) {
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

  public static XpmFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _Magic.Length)
      throw new InvalidDataException("Data too small for a valid XPM file.");

    if (!_ContainsMagic(data))
      throw new InvalidDataException("Invalid XPM magic comment.");

    var text = Encoding.UTF8.GetString(data);
    try {
      return XpmTextParser.Parse(text);
    } catch (InvalidOperationException ex) {
      throw new InvalidDataException(ex.Message, ex);
    }
  }

  public static XpmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static bool _ContainsMagic(ReadOnlySpan<byte> data) {
    // Skip leading whitespace/BOM
    var offset = 0;
    // Skip UTF-8 BOM if present
    if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
      offset = 3;

    while (offset < data.Length && (data[offset] == (byte)' ' || data[offset] == (byte)'\t' || data[offset] == (byte)'\r' || data[offset] == (byte)'\n'))
      ++offset;

    if (offset + _Magic.Length > data.Length)
      return false;

    return data.Slice(offset, _Magic.Length).SequenceEqual(_Magic);
  }
}
