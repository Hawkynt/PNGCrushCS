using System;
using System.Collections.Generic;

namespace FileFormat.KoalaCompressed;

/// <summary>Assembles Commodore 64 compressed Koala file bytes from a KoalaCompressedFile.</summary>
public static class KoalaCompressedWriter {

  public static byte[] ToBytes(KoalaCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var decompressed = new byte[KoalaCompressedFile.DecompressedDataSize];
    var offset = 0;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, KoalaCompressedFile.BitmapDataSize)).CopyTo(decompressed.AsSpan(offset));
    offset += KoalaCompressedFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, KoalaCompressedFile.VideoMatrixSize)).CopyTo(decompressed.AsSpan(offset));
    offset += KoalaCompressedFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, KoalaCompressedFile.ColorRamSize)).CopyTo(decompressed.AsSpan(offset));
    offset += KoalaCompressedFile.ColorRamSize;

    decompressed[offset] = file.BackgroundColor;

    var compressed = _CompressRle(decompressed);

    var result = new byte[KoalaCompressedFile.LoadAddressSize + compressed.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(KoalaCompressedFile.LoadAddressSize));

    return result;
  }

  private static byte[] _CompressRle(byte[] data) {
    var result = new List<byte>(data.Length);
    var i = 0;
    while (i < data.Length) {
      var current = data[i];
      var runLength = 1;
      while (i + runLength < data.Length && data[i + runLength] == current && runLength < 255)
        ++runLength;

      if (runLength >= 3 || current == KoalaCompressedFile.RleEscapeByte) {
        result.Add(KoalaCompressedFile.RleEscapeByte);
        result.Add((byte)runLength);
        result.Add(current);
      } else
        for (var j = 0; j < runLength; ++j)
          result.Add(current);

      i += runLength;
    }

    return result.ToArray();
  }
}
