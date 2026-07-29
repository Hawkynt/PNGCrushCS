using System;
using System.IO;

namespace FileFormat.AtariTools800Font;

/// <summary>Reads AtariTools-800 character sets from bytes, streams, or file paths.</summary>
public static class AtariTools800FontReader {

  public static AtariTools800FontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AtariTools-800 character set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariTools800FontFile FromStream(Stream stream) {
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

  public static AtariTools800FontFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariTools800FontFile.FileSize)
      throw new InvalidDataException($"An AtariTools-800 character set is {AtariTools800FontFile.FileSize} bytes, got {data.Length}.");

    var colors = new byte[AtariTools800FontFile.ColorCount];
    data[..AtariTools800FontFile.ColorCount].CopyTo(colors);

    var font = new byte[AtariTools800FontFile.FontDataSize];
    data[AtariTools800FontFile.ColorCount..].CopyTo(font);

    return new() { Colors = colors, FontData = font };
  }

  public static AtariTools800FontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
