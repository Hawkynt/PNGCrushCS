using System;
using System.Buffers.Binary;

namespace FileFormat.SriSun;

/// <summary>Writes SriSun pictures (.ssi).</summary>
public static class SriSunWriter {

  public static byte[] ToBytes(SriSunFile file) {
    if (file.Width is < 1 or > SriSunFile.MaximumSide || file.Height is < 1 or > SriSunFile.MaximumSide)
      throw new ArgumentOutOfRangeException(nameof(file), $"SriSun dimensions must be between 1 and {SriSunFile.MaximumSide} pixels per side.");
    if (file.Depth is not (1 or 4 or 8 or 16 or 24))
      throw new ArgumentOutOfRangeException(nameof(file), "SriSun depth must be 1, 4, 8, 16 or 24 bits per pixel.");

    var stride = SriSunFile.StrideOf(file.Width, file.Depth);
    var required = checked(stride * file.Height);
    if (file.PixelData == null || file.PixelData.Length < required)
      throw new ArgumentException("The SriSun image does not contain enough pixel data for its dimensions and depth.", nameof(file));

    var result = new byte[checked(SriSunFile.HeaderSize + required)];
    SriSunFile.Magic.CopyTo(result);
    result[SriSunFile.DataTypeAt] = 0;
    result[SriSunFile.DepthAt] = checked((byte)file.Depth);
    result[SriSunFile.MarkerAt] = SriSunFile.Marker;
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(SriSunFile.WidthAt), checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(SriSunFile.HeightAt), checked((ushort)file.Height));
    file.PixelData.AsSpan(0, required).CopyTo(result.AsSpan(SriSunFile.HeaderSize));
    return result;
  }
}
