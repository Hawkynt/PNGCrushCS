using System;

namespace FileFormat.DivGameMap;

/// <summary>Assembles DIV Games Studio FPG file bytes from a DivGameMapFile.</summary>
public static class DivGameMapWriter {

  public static byte[] ToBytes(DivGameMapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var totalSize = DivGameMapFile.MagicSize + DivGameMapFile.PaletteSize + DivGameMapFile.EntryHeaderSize + pixelCount;
    var result = new byte[totalSize];
    var offset = 0;

    // Magic
    DivGameMapFile.Magic.AsSpan(0, DivGameMapFile.MagicSize).CopyTo(result.AsSpan(offset));
    offset += DivGameMapFile.MagicSize;

    // Palette
    file.Palette.AsSpan(0, DivGameMapFile.PaletteSize).CopyTo(result.AsSpan(offset));
    offset += DivGameMapFile.PaletteSize;

    // Entry header: code(4) + length(4) + description(32) + filename(12) + width(4) + height(4) + numPoints(4)
    offset += 4; // code = 0
    BitConverter.TryWriteBytes(new Span<byte>(result, offset, 4), pixelCount);
    offset += 4;
    offset += 32; // description
    offset += 12; // filename
    BitConverter.TryWriteBytes(new Span<byte>(result, offset, 4), file.Width);
    offset += 4;
    BitConverter.TryWriteBytes(new Span<byte>(result, offset, 4), file.Height);
    offset += 4;
    offset += 4; // numPoints = 0

    // Pixel data
    file.PixelData.AsSpan(0, pixelCount).CopyTo(result.AsSpan(offset));

    return result;
  }
}
