using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Assembles Tiny (compressed DEGAS) file bytes from a TinyFile.</summary>
public static class TinyWriter {

  public static byte[] ToBytes(TinyFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var (planeCount, wordsPerPlane) = _GetPlaneInfo(file.Resolution);
    var compressedData = TinyCompressor.Compress(file.PixelData, planeCount, wordsPerPlane);

    using var ms = new MemoryStream();

    ms.WriteByte((byte)file.Resolution);

    Span<byte> buf = stackalloc byte[2];
    for (var i = 0; i < 16; ++i) {
      BinaryPrimitives.WriteInt16BigEndian(buf, file.Palette[i]);
      ms.Write(buf);
    }

    ms.Write(compressedData);

    return ms.ToArray();
  }

  private static (int PlaneCount, int WordsPerPlane) _GetPlaneInfo(TinyResolution resolution) => resolution switch {
    TinyResolution.Low => (4, 4000),
    TinyResolution.Medium => (2, 8000),
    TinyResolution.High => (1, 16000),
    _ => throw new InvalidDataException($"Unknown Tiny resolution: {resolution}.")
  };
}
