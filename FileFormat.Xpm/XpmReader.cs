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

  public static XpmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
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

  private static bool _ContainsMagic(byte[] data) {
    var span = data.AsSpan();
    // Skip leading whitespace/BOM
    var offset = 0;
    // Skip UTF-8 BOM if present
    if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
      offset = 3;

    while (offset < span.Length && (span[offset] == (byte)' ' || span[offset] == (byte)'\t' || span[offset] == (byte)'\r' || span[offset] == (byte)'\n'))
      ++offset;

    if (offset + _Magic.Length > span.Length)
      return false;

    return span.Slice(offset, _Magic.Length).SequenceEqual(_Magic);
  }
}
