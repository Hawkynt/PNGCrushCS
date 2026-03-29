using System;
using System.IO;
using System.Text;

namespace FileFormat.Xbm;

/// <summary>Reads XBM files from bytes, streams, or file paths.</summary>
public static class XbmReader {

  private const int _MINIMUM_SIZE = 20;

  public static XbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XbmFile FromStream(Stream stream) {
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

  public static XbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MINIMUM_SIZE)
      throw new InvalidDataException("Data too small for a valid XBM file.");

    var text = Encoding.ASCII.GetString(data);

    if (!text.Contains("#define"))
      throw new InvalidDataException("Invalid XBM format: no #define directives found.");

    return XbmTextParser.Parse(text);
  }
}
