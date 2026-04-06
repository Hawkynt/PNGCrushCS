using System;
using System.IO;

namespace FileFormat.FalconRes;

/// <summary>Reads Falcon Res screen dumps from bytes, streams, or file paths.</summary>
public static class FalconResReader {

  /// <summary>The exact file size of a valid Falcon Res screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = FalconResFile.ExpectedFileSize;

  public static FalconResFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Falcon Res file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FalconResFile FromStream(Stream stream) {
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

  public static FalconResFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static FalconResFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid Falcon Res data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new FalconResFile {
      PixelData = pixelData
    };
  }
}
