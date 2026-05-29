using System;

namespace FileFormat.HomeworldLif;

/// <summary>Assembles Homeworld LIF texture file bytes from a HomeworldLifFile.</summary>
public static class HomeworldLifWriter {

  public static byte[] ToBytes(HomeworldLifFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.Width * file.Height * 4;
    var result = new byte[HomeworldLifFile.HeaderSize + pixelDataSize];

    // Magic
    HomeworldLifFile.Magic.AsSpan(0, 4).CopyTo(result.AsSpan(0));

    // Version
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 4), file.Version);

    // Width
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 4), file.Width);

    // Height
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 4), file.Height);

    // Pixel data
    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(HomeworldLifFile.HeaderSize));

    return result;
  }
}
