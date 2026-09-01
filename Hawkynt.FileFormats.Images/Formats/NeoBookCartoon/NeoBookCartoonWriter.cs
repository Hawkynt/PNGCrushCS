using System;
using System.Buffers.Binary;

namespace FileFormat.NeoBookCartoon;

/// <summary>Writes the independently verified one-picture NeoBook cartoon subset.</summary>
public static class NeoBookCartoonWriter {

  public static byte[] ToBytes(NeoBookCartoonFile file) {
    if (file.PictureOffset < NeoBookCartoonFile.HeaderSize)
      throw new ArgumentOutOfRangeException(nameof(file), $"NeoBook picture offset must be at least {NeoBookCartoonFile.HeaderSize}.");
    if (file.Picture is null || file.Picture.Length == 0)
      throw new ArgumentException("NeoBook cartoon must contain a PNG picture.", nameof(file));

    var pngLength = NeoBookCartoonReader.PngLength(file.Picture);
    if (pngLength != file.Picture.Length)
      throw new ArgumentException("NeoBook picture must be exactly one complete PNG ending at IEND.", nameof(file));

    var result = new byte[checked(file.PictureOffset + file.Picture.Length)];
    NeoBookCartoonFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2, 4), file.PictureOffset);
    file.Picture.CopyTo(result, file.PictureOffset);
    return result;
  }
}
