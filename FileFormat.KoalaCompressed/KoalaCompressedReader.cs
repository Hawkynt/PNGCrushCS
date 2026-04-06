using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.KoalaCompressed;

/// <summary>Reads Commodore 64 compressed Koala files from bytes, streams, or file paths.</summary>
public static class KoalaCompressedReader {

  public static KoalaCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Compressed Koala file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KoalaCompressedFile FromStream(Stream stream) {
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

  public static KoalaCompressedFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static KoalaCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < KoalaCompressedFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid compressed Koala file (minimum {KoalaCompressedFile.MinFileSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var decompressed = _DecompressRle(data, KoalaCompressedFile.LoadAddressSize);
    if (decompressed.Length < KoalaCompressedFile.DecompressedDataSize)
      throw new InvalidDataException($"Decompressed data too small (expected {KoalaCompressedFile.DecompressedDataSize} bytes, got {decompressed.Length}).");

    var offset = 0;

    var bitmapData = new byte[KoalaCompressedFile.BitmapDataSize];
    decompressed.AsSpan(offset, KoalaCompressedFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += KoalaCompressedFile.BitmapDataSize;

    var videoMatrix = new byte[KoalaCompressedFile.VideoMatrixSize];
    decompressed.AsSpan(offset, KoalaCompressedFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += KoalaCompressedFile.VideoMatrixSize;

    var colorRam = new byte[KoalaCompressedFile.ColorRamSize];
    decompressed.AsSpan(offset, KoalaCompressedFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += KoalaCompressedFile.ColorRamSize;

    var backgroundColor = decompressed[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }

  private static byte[] _DecompressRle(byte[] data, int startOffset) {
    var result = new List<byte>(KoalaCompressedFile.DecompressedDataSize);
    var i = startOffset;
    while (i < data.Length) {
      var b = data[i++];
      if (b == KoalaCompressedFile.RleEscapeByte) {
        if (i + 1 >= data.Length)
          break;

        var count = data[i++];
        var value = data[i++];
        for (var j = 0; j < count; ++j)
          result.Add(value);
      } else
        result.Add(b);
    }

    return result.ToArray();
  }
}
