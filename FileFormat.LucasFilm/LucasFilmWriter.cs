using System;

namespace FileFormat.LucasFilm;

/// <summary>Assembles LucasFilm LFF file bytes from a LucasFilmFile.</summary>
public static class LucasFilmWriter {

  public static byte[] ToBytes(LucasFilmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[LucasFilmFile.HeaderSize + pixelDataSize];

    result[0] = LucasFilmFile.Magic[0];
    result[1] = LucasFilmFile.Magic[1];
    result[2] = LucasFilmFile.Magic[2];
    result[3] = LucasFilmFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Channels);
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 4), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(LucasFilmFile.HeaderSize));

    return result;
  }
}
