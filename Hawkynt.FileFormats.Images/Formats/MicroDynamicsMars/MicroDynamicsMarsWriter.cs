using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.MicroDynamicsMars;

/// <summary>Writes Micro Dynamics MARS pages (.pbt).</summary>
public static class MicroDynamicsMarsWriter {

  public static byte[] ToBytes(MicroDynamicsMarsFile file) {
    if (file.Width is < 1 or > MicroDynamicsMarsFile.MaximumSide || file.Height is < 1 or > MicroDynamicsMarsFile.MaximumSide)
      throw new ArgumentOutOfRangeException(nameof(file), $"Micro Dynamics MARS dimensions must be between 1 and {MicroDynamicsMarsFile.MaximumSide} pixels per side.");

    var required = checked(((file.Width + 7) / 8) * file.Height);
    if (file.PixelData == null || file.PixelData.Length < required)
      throw new ArgumentException("The Micro Dynamics MARS page does not contain enough packed bilevel data for its dimensions.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    var result = new byte[checked(MicroDynamicsMarsFile.HeaderSize + coded.Length)];
    MicroDynamicsMarsFile.Signature.CopyTo(result);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(MicroDynamicsMarsFile.ResolutionOffset), file.Resolution);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(MicroDynamicsMarsFile.HeightOffset), file.Height);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(MicroDynamicsMarsFile.WidthOffset), file.Width);
    coded.CopyTo(result, MicroDynamicsMarsFile.HeaderSize);
    return result;
  }
}
