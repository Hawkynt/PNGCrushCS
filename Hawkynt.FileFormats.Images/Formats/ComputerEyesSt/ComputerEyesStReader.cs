using System;
using System.IO;

namespace FileFormat.ComputerEyesSt;

/// <summary>Reads ComputerEyes ST captures from bytes, streams, or file paths.</summary>
public static class ComputerEyesStReader {

  public static ComputerEyesStFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Capture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ComputerEyesStFile FromStream(Stream stream) {
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

  public static ComputerEyesStFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 192022 || data[0] != 'E' || data[1] != 'Y' || data[2] != 'E' || data[3] != 'S' || data[4] != 0)
      throw new InvalidDataException("Not a ComputerEyes capture.");

    var (kind, size) = data[5] switch {
      0 => (ComputerEyesStKind.Color, 192022),
      1 => (ComputerEyesStKind.HighResolutionColor, 256022),
      2 => (ComputerEyesStKind.Grey, 256022),
      _ => throw new InvalidDataException($"Capture mode {data[5]} is not one of the three that exist."),
    };

    if (data.Length != size)
      throw new InvalidDataException($"A capture in mode {data[5]} is {size} bytes, not {data.Length}.");

    return new() { Data = data.ToArray(), Kind = kind };
  }

  public static ComputerEyesStFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
