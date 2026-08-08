using System;
using System.Buffers.Binary;

namespace FileFormat.InterleafImage;

/// <summary>Assembles an Interleaf image: the header, then the planes a line at a time.</summary>
/// <remarks>
/// Six of the header's thirty-one bytes have no meaning anyone here has established — a word at 4, a
/// run of <c>01 02 03 04</c> at 10 and a word at 24. They are written as the one sample has them,
/// which is the only thing about them that is known to be right.
/// </remarks>
public static class InterleafImageWriter {

  /// <summary>The header bytes whose meaning is not established, exactly as the sample carries them.</summary>
  private static ReadOnlySpan<byte> UnknownAt4 => [0x00, 0x04];
  private static ReadOnlySpan<byte> UnknownAt10 => [0x01, 0x02, 0x03, 0x04];
  private static ReadOnlySpan<byte> UnknownAt24 => [0x00, 0x29];

  public static byte[] ToBytes(InterleafImageFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid Interleaf image size: {width}x{height}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height * InterleafImageFile.PlaneCount];
    var result = new byte[InterleafImageFile.HeaderSize + width * height * InterleafImageFile.PlaneCount];

    InterleafImageFile.Magic.CopyTo(result);
    UnknownAt4.CopyTo(result.AsSpan(4));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(InterleafImageFile.HorizontalResolutionAt), (ushort)(file.HorizontalResolution < 1 ? 75 : file.HorizontalResolution));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(InterleafImageFile.VerticalResolutionAt), (ushort)(file.VerticalResolution < 1 ? 75 : file.VerticalResolution));
    UnknownAt10.CopyTo(result.AsSpan(10));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(InterleafImageFile.WidthAt), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(InterleafImageFile.HeightAt), (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(InterleafImageFile.BitsPerPixelAt), InterleafImageFile.SupportedBitsPerPixel);
    UnknownAt24.CopyTo(result.AsSpan(24));

    var body = result.AsSpan(InterleafImageFile.HeaderSize);
    for (var y = 0; y < height; ++y) {
      var line = y * width * InterleafImageFile.PlaneCount;
      for (int x = 0, at = line; x < width; ++x, at += InterleafImageFile.PlaneCount) {
        body[line + x] = pixels[at];
        body[line + width + x] = pixels[at + 1];
        body[line + width * 2 + x] = pixels[at + 2];
      }
    }

    return result;
  }
}
