using System;

namespace FileFormat.NewsRoom;

/// <summary>Assembles NewsRoom panel bytes from a <see cref="NewsRoomFile"/>.</summary>
public static class NewsRoomWriter {

  public static byte[] ToBytes(NewsRoomFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Width < 1 || file.Height < 1 || file.Width % 8 != 0 || file.Height % 8 != 0)
      throw new ArgumentException(
        $"A NewsRoom panel is written at a size that is a whole number of bytes across and down; {file.Width}x{file.Height} is not.",
        nameof(file));

    // The header states the two sizes as coordinate pairs, and both ends have to fit in a byte.
    if (file.Width > NewsRoomFile.MaximumWidth || file.Height > NewsRoomFile.MaximumHeight)
      throw new ArgumentException(
        $"A NewsRoom panel states its size in single bytes, so {file.Width}x{file.Height} cannot be written.",
        nameof(file));

    var stride = NewsRoomFile.StrideOf(file.Width);
    var result = new byte[NewsRoomFile.HeaderSize + stride * file.Height];
    result[0] = NewsRoomFile.Signature[0];
    result[1] = NewsRoomFile.Signature[1];
    result[NewsRoomFile.HeightPairOffset] = 0;
    result[NewsRoomFile.HeightPairOffset + 1] = (byte)file.Height;
    result[NewsRoomFile.WidthPairOffset] = 0;
    result[NewsRoomFile.WidthPairOffset + 1] = (byte)(file.Width - 1);
    result[NewsRoomFile.LowMarkerOffset] = 0x00;
    result[NewsRoomFile.HighMarkerOffset] = 0xFF;

    var bits = file.PixelData ?? [];
    bits.AsSpan(0, Math.Min(bits.Length, result.Length - NewsRoomFile.HeaderSize))
      .CopyTo(result.AsSpan(NewsRoomFile.HeaderSize));

    return result;
  }
}
