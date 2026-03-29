using System;

namespace FileFormat.FullscreenKit;

/// <summary>Assembles Fullscreen Construction Kit (.kid) file bytes from an in-memory representation.</summary>
public static class FullscreenKitWriter {

  public static byte[] ToBytes(FullscreenKitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var header = FullscreenKitHeader.FromPalette(file.Palette);
    var totalSize = FullscreenKitHeader.StructSize + file.PixelData.Length;
    var result = new byte[totalSize];
    header.WriteTo(result.AsSpan());
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(FullscreenKitHeader.StructSize));

    return result;
  }
}
