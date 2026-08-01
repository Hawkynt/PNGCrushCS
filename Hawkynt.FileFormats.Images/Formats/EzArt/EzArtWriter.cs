using System;
using FileFormat.Core;

namespace FileFormat.EzArt;

/// <summary>Assembles EZ-Art Professional (.eza) file bytes.</summary>
/// <remarks>
/// Four bytes naming the format, the palette, forty-four bytes in all, and then the screen packed
/// with the run-length coding Apple named. What this wrote before was the palette and the bare
/// screen with no name in front of it and nothing packed — a shape nothing that knows the format
/// would look at twice.
/// </remarks>
public static class EzArtWriter {

  public static byte[] ToBytes(EzArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var planeRows = AtariStGraphics.ToPlaneRows(
      file.PixelData ?? [], EzArtFile.ScreenWidth, EzArtFile.ScreenHeight, EzArtFile.Planes);
    var packed = PackBits.Pack(planeRows);

    var result = new byte[EzArtFile.HeaderSize + packed.Length];
    EzArtFile.Signature.CopyTo(result.AsSpan());

    for (var i = 0; i < EzArtFile.PaletteColors; ++i) {
      var word = i < (file.Palette?.Length ?? 0) ? file.Palette![i] : (short)0;
      result[EzArtFile.PaletteOffset + i * 2] = (byte)(word >> 8);
      result[EzArtFile.PaletteOffset + i * 2 + 1] = (byte)word;
    }

    packed.CopyTo(result, EzArtFile.HeaderSize);
    return result;
  }
}
