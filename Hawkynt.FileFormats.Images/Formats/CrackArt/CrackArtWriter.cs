using System;

namespace FileFormat.CrackArt;

/// <summary>Assembles CrackArt file bytes from a CrackArtFile.</summary>
public static class CrackArtWriter {

  public static byte[] ToBytes(CrackArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var compressedData = CrackArtCompressor.Compress(file.PixelData);
    var dataOffset = CrackArtHeader.GetDataOffset(file.Resolution);

    var result = new byte[dataOffset + compressedData.Length];
    CrackArtHeader.Write(result, isCompressed: true, file.Resolution, file.Palette);
    compressedData.AsSpan().CopyTo(result.AsSpan(dataOffset));

    return result;
  }
}
