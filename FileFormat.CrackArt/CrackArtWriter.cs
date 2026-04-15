using System;

namespace FileFormat.CrackArt;

/// <summary>Assembles CrackArt file bytes from a CrackArtFile.</summary>
public static class CrackArtWriter {

  public static byte[] ToBytes(CrackArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var header = new CrackArtHeader((byte)file.Resolution, file.Palette);
    var compressedData = CrackArtCompressor.Compress(file.PixelData);

    var result = new byte[CrackArtHeader.StructSize + compressedData.Length];
    header.WriteTo(result.AsSpan());
    compressedData.AsSpan(0, compressedData.Length).CopyTo(result.AsSpan(CrackArtHeader.StructSize));

    return result;
  }
}
