using System;
using System.IO;

namespace FileFormat.BbcMicro;

/// <summary>Reads BBC Micro screen memory dumps from bytes, streams, or file paths.</summary>
public static class BbcMicroReader {

  public static BbcMicroFile FromFile(FileInfo file, BbcMicroMode mode = BbcMicroMode.Mode1) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BBC Micro screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), mode);
  }

  public static BbcMicroFile FromStream(Stream stream, BbcMicroMode mode = BbcMicroMode.Mode1) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data, mode);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray(), mode);
  }

  public static BbcMicroFile FromBytes(byte[] data, BbcMicroMode mode = BbcMicroMode.Mode1) {
    ArgumentNullException.ThrowIfNull(data);

    var expectedSize = BbcMicroFile.GetExpectedScreenSize(mode);
    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for a valid BBC Micro mode {(int)mode} screen dump. Expected {expectedSize} bytes, got {data.Length}.");

    if (data.Length != expectedSize)
      throw new InvalidDataException($"Invalid BBC Micro screen dump size. Expected exactly {expectedSize} bytes for mode {(int)mode}, got {data.Length}.");

    var width = BbcMicroFile.GetWidth(mode);
    var height = BbcMicroFile.FixedHeight;

    // Convert from character-block layout to linear scanline order
    var linearData = BbcMicroLayoutConverter.CharacterBlockToLinear(data, width, height, mode);

    return new() {
      Width = width,
      Height = height,
      Mode = mode,
      PixelData = linearData
    };
  }
}
