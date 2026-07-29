using System;

namespace FileFormat.EzArt;

/// <summary>Assembles EZ-Art Professional (.eza) file bytes from an EzArtFile.</summary>
public static class EzArtWriter {

  public static byte[] ToBytes(EzArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EzArtFile.FileSize];

    new EzArtHeader(file.Palette).WriteTo(result);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(EzArtHeader.StructSize));

    return result;
  }
}
