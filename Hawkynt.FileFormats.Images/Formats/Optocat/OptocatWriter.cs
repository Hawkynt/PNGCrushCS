using System;
using System.Buffers.Binary;

namespace FileFormat.Optocat;

/// <summary>Writes Optocat pictures as uncompressed three-sample RGB rows.</summary>
public static class OptocatWriter {

  public static byte[] ToBytes(OptocatFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Optocat dimensions must fit 16-bit fields; got {file.Width}x{file.Height}.", nameof(file));
    if (file.SamplesPerPixel != 3)
      throw new ArgumentException("The Optocat writer emits the lossless three-sample RGB form.", nameof(file));

    var offset = file.PixelOffset < OptocatFile.MinimumOffset ? OptocatFile.MinimumOffset : file.PixelOffset;
    if (offset > ushort.MaxValue)
      throw new ArgumentException($"Optocat stores the pixel offset in 16 bits; got {offset}.", nameof(file));

    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Optocat needs {expected} RGB bytes.", nameof(file));

    var output = new byte[checked(offset + expected)];
    output[0] = (byte)'I';
    output[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4, 2), (ushort)offset);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(10, 2), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(14, 2), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(16, 2), (ushort)file.Height);
    file.PixelData.AsSpan(0, expected).CopyTo(output.AsSpan(offset));
    return output;
  }
}
