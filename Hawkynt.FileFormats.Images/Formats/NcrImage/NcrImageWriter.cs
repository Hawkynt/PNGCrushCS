using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.NcrImage;

/// <summary>Writes the verified NCR header followed by CCITT Group 4 coding.</summary>
public static class NcrImageWriter {

  public static byte[] ToBytes(NcrImageFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"NCR dimensions must fit 16-bit fields; got {file.Width}x{file.Height}.", nameof(file));
    var bytesPerRow = checked((file.Width + 7) / 8);
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"NCR needs {expected} packed 1bpp bytes.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    var output = new byte[checked(NcrImageFile.CodedDataOffset + coded.Length)];
    NcrImageFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(NcrImageFile.WidthOffset, 2), checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(NcrImageFile.HeightOffset, 2), checked((ushort)file.Height));
    output[NcrImageFile.CodingOffset] = NcrImageReader.CodingGroup4;
    coded.CopyTo(output, NcrImageFile.CodedDataOffset);
    return output;
  }
}
