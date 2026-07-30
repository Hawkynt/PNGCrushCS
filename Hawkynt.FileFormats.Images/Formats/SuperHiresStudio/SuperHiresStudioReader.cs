using System;
using System.IO;

namespace FileFormat.SuperHiresStudio;

/// <summary>Reads Super Hires Studio pictures from bytes, streams, or file paths.</summary>
public static class SuperHiresStudioReader {

  public static SuperHiresStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super Hires Studio picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresStudioFile FromStream(Stream stream) {
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

  public static SuperHiresStudioFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SuperHiresStudioFile.FileSize)
      throw new InvalidDataException($"A Super Hires Studio picture is {SuperHiresStudioFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static SuperHiresStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
