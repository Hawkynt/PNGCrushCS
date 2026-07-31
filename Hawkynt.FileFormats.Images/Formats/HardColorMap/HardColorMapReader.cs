using System;
using System.IO;
using System.Text;

namespace FileFormat.HardColorMap;

/// <summary>Reads Hard Color Map pictures from bytes, streams, or file paths.</summary>
public static class HardColorMapReader {

  public static HardColorMapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HardColorMapFile FromStream(Stream stream) {
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

  public static HardColorMapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != HardColorMapFile.FileSize
        || Encoding.ASCII.GetString(data[..HardColorMapFile.Signature.Length]) != HardColorMapFile.Signature
        || data[5] != 1)
      throw new InvalidDataException("Not a Hard Color Map picture.");

    // The file names one of two arrangements, which differ in both the priority ranking and which
    // sprite ends up on the left — the two are not independent choices.
    var (left, priority) = data[6] switch {
      0 => (2, 0),
      2 => (1, 36),
      _ => throw new InvalidDataException($"A Hard Color Map picture is arranged 0 or 2, not {data[6]}."),
    };

    return new() { Data = data.ToArray(), LeftSprite = left, Priority = priority };
  }

  public static HardColorMapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
