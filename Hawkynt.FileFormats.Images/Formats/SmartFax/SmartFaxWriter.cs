using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.SmartFax;

/// <summary>Assembles SmartFax page bytes from a <see cref="SmartFaxFile"/>.</summary>
public static class SmartFaxWriter {

  public static byte[] ToBytes(SmartFaxFile file) {
    var pixelData = file.PixelData ?? [];
    var coded = CcittG3Encoder.Encode(pixelData, file.Width, file.Height, leadingEndOfLine: true);
    var reversed = CcittFillOrder.Reverse(coded);

    var result = new byte[SmartFaxFile.HeaderSize + reversed.Length];
    SmartFaxFile.Signature.CopyTo(result.AsSpan(0));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(SmartFaxFile.BytesPerRowOffset), (ushort)(file.Width / 8));
    result[SmartFaxFile.ResolutionOffset] =
      (byte)(file.VerticalResolution == SmartFaxFile.CoarseResolution ? 0 : 1);
    reversed.CopyTo(result.AsSpan(SmartFaxFile.HeaderSize));

    return result;
  }
}
