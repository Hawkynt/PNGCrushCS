using System;

namespace FileFormat.AtariPaintworks;

/// <summary>Assembles Atari ST Paintworks/GFA/DeskPic file bytes from an AtariPaintworksFile.</summary>
public static class AtariPaintworksWriter {

  public static byte[] ToBytes(AtariPaintworksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariPaintworksFile.FileSize];
    var span = result.AsSpan();

    new AtariPaintworksHeader(file.Palette).WriteTo(span[AtariPaintworksFile.PaletteOffset..]);
    AtariPaintworksFile.Signature.CopyTo(span[AtariPaintworksFile.SignatureOffset..]);
    span[AtariPaintworksFile.FlagsOffset] = AtariPaintworksFile.LowResolutionFlags;

    file.PixelData.AsSpan(0, Math.Min(AtariPaintworksFile.BitmapDataSize, file.PixelData.Length))
      .CopyTo(span[AtariPaintworksFile.BitmapOffset..]);

    return result;
  }
}
