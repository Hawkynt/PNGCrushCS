using System;
using System.IO;

namespace FileFormat.PntrFalcon;

/// <summary>Reads PntrFalcon screen dumps from bytes, streams, or file paths.</summary>
public static class PntrFalconReader {

  /// <summary>The exact file size of a valid PntrFalcon screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = PntrFalconFile.ExpectedFileSize;

  public static PntrFalconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PntrFalcon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PntrFalconFile FromStream(Stream stream) {
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

  public static PntrFalconFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PntrFalconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid PntrFalcon data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new PntrFalconFile {
      PixelData = pixelData
    };
  }
}
