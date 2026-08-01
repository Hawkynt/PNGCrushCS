using System;
using System.IO;
using System.Text;

namespace FileFormat.FullscreenKit;

/// <summary>Assembles a Fullscreen Construction Kit picture: two marker bytes, the palette, the bitplanes.</summary>
public static class FullscreenKitWriter {

  public static byte[] ToBytes(FullscreenKitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FullscreenKitFile.FileSize];
    Encoding.ASCII.GetBytes(FullscreenKitFile.Signature).CopyTo(result.AsSpan(0));

    var palette = file.Palette ?? [];
    for (var i = 0; i < FullscreenKitFile.ColorCount; ++i) {
      var value = i < palette.Length ? palette[i] : (short)0;
      result[FullscreenKitFile.PaletteOffset + i * 2] = (byte)(value >> 8);
      result[FullscreenKitFile.PaletteOffset + i * 2 + 1] = (byte)value;
    }

    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, FullscreenKitFile.FileSize - FullscreenKitFile.BitmapOffset))
      .CopyTo(result.AsSpan(FullscreenKitFile.BitmapOffset));

    return result;
  }

  public static void ToFile(FullscreenKitFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
