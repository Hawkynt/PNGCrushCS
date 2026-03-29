using System;
using System.IO;

namespace FileFormat.RagePaint;

/// <summary>Reads Rage Paint screen dumps from bytes, streams, or file paths.</summary>
public static class RagePaintReader {

  /// <summary>The exact file size of a valid Rage Paint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = RagePaintFile.ExpectedFileSize;

  public static RagePaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Rage Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RagePaintFile FromStream(Stream stream) {
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

  public static RagePaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid Rage Paint data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new RagePaintFile {
      PixelData = pixelData
    };
  }
}
