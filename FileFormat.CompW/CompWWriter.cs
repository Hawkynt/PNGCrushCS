using System;

namespace FileFormat.CompW;

/// <summary>Assembles CompW WLM file bytes from a CompWFile.</summary>
public static class CompWWriter {

  public static byte[] ToBytes(CompWFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var result = new byte[CompWFile.HeaderSize + pixelCount + CompWFile.PaletteSize];

    result[0] = CompWFile.Magic[0];
    result[1] = CompWFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.BitsPerPixel);

    file.PixelData.AsSpan(0, pixelCount).CopyTo(result.AsSpan(CompWFile.HeaderSize));
    file.Palette.AsSpan(0, CompWFile.PaletteSize).CopyTo(result.AsSpan(CompWFile.HeaderSize + pixelCount));

    return result;
  }
}
