using System;

namespace FileFormat.G9b;

/// <summary>Assembles V9990 GFX9000 (.g9b) file bytes from a <see cref="G9bFile"/>.</summary>
public static class G9bWriter {

  public static byte[] ToBytes(G9bFile file) {
    var palette = file.Palette ?? [];
    var result = new byte[G9bFile.FixedHeaderSize + palette.Length + file.Stride * file.Height];

    result[0] = (byte)'G';
    result[1] = (byte)'9';
    result[2] = (byte)'B';
    result[3] = G9bFile.Version;
    result[4] = 0;
    result[5] = (byte)file.Depth;
    result[6] = file.ColorMode;
    result[7] = (byte)(palette.Length / 3);
    result[8] = (byte)file.Width;
    result[9] = (byte)(file.Width >> 8);
    result[10] = (byte)file.Height;
    result[11] = (byte)(file.Height >> 8);

    // Stored as it lies. The packed form saves space on pictures with long runs and costs a decoder
    // that has none here; a reader that handles both will take this one.
    result[12] = 0;

    palette.CopyTo(result.AsSpan(G9bFile.FixedHeaderSize));

    var bitmap = file.PixelData ?? [];
    var length = Math.Min(result.Length - file.BitmapOffset, bitmap.Length);
    bitmap.AsSpan(0, length).CopyTo(result.AsSpan(file.BitmapOffset));

    return result;
  }
}
