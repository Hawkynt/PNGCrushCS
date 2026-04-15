using System;

namespace FileFormat.SyntheticArts;

/// <summary>Assembles Synthetic Arts (.srt) file bytes from an in-memory representation.</summary>
public static class SyntheticArtsWriter {

  public static byte[] ToBytes(SyntheticArtsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var header = new SyntheticArtsHeader(file.Palette);
    var result = new byte[SyntheticArtsFile.FileSize];
    header.WriteTo(result.AsSpan());
    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(SyntheticArtsHeader.StructSize));

    return result;
  }
}
