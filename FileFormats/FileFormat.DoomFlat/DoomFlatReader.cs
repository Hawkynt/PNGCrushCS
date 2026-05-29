using System;
using System.IO;

namespace FileFormat.DoomFlat;

/// <summary>Reads Doom flat texture lump files from bytes, streams, or file paths.</summary>
public static class DoomFlatReader {

  public static DoomFlatFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DoomFlat file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DoomFlatFile FromStream(Stream stream) {
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

  public static DoomFlatFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != DoomFlatFile.FileSize)
      throw new InvalidDataException($"Invalid DoomFlat data size: expected exactly {DoomFlatFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DoomFlatFile.FileSize];
    data.Slice(0, DoomFlatFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static DoomFlatFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != DoomFlatFile.FileSize)
      throw new InvalidDataException($"Invalid DoomFlat data size: expected exactly {DoomFlatFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DoomFlatFile.FileSize];
    data.AsSpan(0, DoomFlatFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
