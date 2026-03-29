using System;
using System.IO;

namespace FileFormat.DoodlePacked;

/// <summary>Reads Doodle Packed (RLE-compressed C64 hires) files from bytes, streams, or file paths.</summary>
public static class DoodlePackedReader {

  /// <summary>Minimum valid file size: 2-byte load address + at least 1 byte of RLE data.</summary>
  internal const int MinFileSize = DoodlePackedFile.LoadAddressSize + 1;

  public static DoodlePackedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Doodle Packed file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DoodlePackedFile FromStream(Stream stream) {
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

  public static DoodlePackedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinFileSize)
      throw new InvalidDataException($"Data too small for Doodle Packed file (got {data.Length} bytes, need at least {MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += DoodlePackedFile.LoadAddressSize;

    // RLE-compressed payload
    var compressed = new byte[data.Length - offset];
    data.AsSpan(offset, compressed.Length).CopyTo(compressed.AsSpan(0));

    var decompressed = DoodlePackedFile.RleDecode(compressed);
    if (decompressed.Length < DoodlePackedFile.DecompressedSize)
      throw new InvalidDataException($"Decompressed data too small (got {decompressed.Length} bytes, expected {DoodlePackedFile.DecompressedSize}).");

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[DoodlePackedFile.BitmapDataSize];
    decompressed.AsSpan(0, DoodlePackedFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    // Screen RAM (1000 bytes)
    var screenData = new byte[DoodlePackedFile.ScreenDataSize];
    decompressed.AsSpan(DoodlePackedFile.BitmapDataSize, DoodlePackedFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
    };
  }
}
