using System;
using System.IO;

namespace FileFormat.NokiaNlm;

/// <summary>Reads Nokia Logo Manager image files from bytes, streams, or file paths.</summary>
public static class NokiaNlmReader {

  public static NokiaNlmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NokiaNlm file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaNlmFile FromStream(Stream stream) {
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

  public static NokiaNlmFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NokiaNlmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != NokiaNlmFile.FileSize)
      throw new InvalidDataException($"Invalid NokiaNlm data size: expected exactly {NokiaNlmFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[NokiaNlmFile.FileSize];
    data.AsSpan(0, NokiaNlmFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
