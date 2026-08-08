using System;
using System.Buffers.Binary;

namespace FileFormat.Vivid;

/// <summary>Assembles a Vivid picture: the size, then each row numbered and split into its planes.</summary>
/// <remarks>
/// A row is not interleaved. It states its own number and then carries all of its red, all of its
/// green and all of its blue in turn, which is what the reader takes it apart into.
/// </remarks>
public static class VividWriter {

  public static byte[] ToBytes(VividFile file) {
    var pixels = file.PixelData ?? [];
    var stride = VividFile.RowNumberSize + file.Width * 3;
    var result = new byte[VividFile.HeaderSize + stride * file.Height];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);

    for (var y = 0; y < file.Height; ++y) {
      var at = VividFile.HeaderSize + y * stride;
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), (ushort)y);

      var row = at + VividFile.RowNumberSize;
      for (var x = 0; x < file.Width; ++x) {
        var from = (y * file.Width + x) * 3;
        if (from + 2 >= pixels.Length)
          continue;

        result[row + x] = pixels[from];
        result[row + file.Width + x] = pixels[from + 1];
        result[row + file.Width * 2 + x] = pixels[from + 2];
      }
    }

    return result;
  }
}
