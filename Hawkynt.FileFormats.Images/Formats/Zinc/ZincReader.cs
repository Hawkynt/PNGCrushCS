using System;
using System.IO;
using System.Text;

namespace FileFormat.Zinc;

/// <summary>Reads Zinc Interface Library bitmap source files.</summary>
public static class ZincReader {

  private const int _MinimumSize = 20;

  public static ZincFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zinc bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZincFile FromStream(Stream stream) {
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

  public static ZincFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ZincFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MinimumSize)
      throw new InvalidDataException("Data too small for a valid Zinc bitmap.");

    var text = Encoding.ASCII.GetString(data);
    if (!text.Contains("USHORT", StringComparison.Ordinal))
      throw new InvalidDataException("Invalid Zinc bitmap: no USHORT array declaration found.");

    return ZincTextParser.Parse(text);
  }
}
