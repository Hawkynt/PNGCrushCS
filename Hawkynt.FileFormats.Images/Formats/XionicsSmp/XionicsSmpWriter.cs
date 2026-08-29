using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.XionicsSmp;

/// <summary>Writes Xionics SMP pages using the verified reversed-fill-order Group 4 coding.</summary>
public static class XionicsSmpWriter {

  public static byte[] ToBytes(XionicsSmpFile file) {
    if (file.Width is < 8 or > 65528 || (file.Width & 7) != 0 || file.Height is < 1 or > 65535)
      throw new ArgumentException($"Xionics SMP requires a byte-aligned width up to 65528 and height up to 65535; got {file.Width}x{file.Height}.", nameof(file));

    var bytesPerRow = file.Width >> 3;
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Xionics SMP needs {expected} packed 1bpp bytes.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    var reversed = CcittFillOrder.Reverse(coded);
    var output = new byte[checked(XionicsSmpFile.HeaderSize + reversed.Length)];

    XionicsSmpFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.OneOffset, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.CompressionOffset, 2), XionicsSmpFile.CompressionGroup4);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.BytesPerRowOffset, 2), (ushort)bytesPerRow);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.HeightOffset, 2), (ushort)file.Height);

    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.EscapeOffset, 2), 0x1B);
    output[XionicsSmpFile.HorizontalTagOffset] = 0x19;
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.HorizontalTagOffset + 1, 2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.HorizontalResolutionOffset, 2), (ushort)Math.Clamp(file.HorizontalResolution, 0, ushort.MaxValue));
    output[XionicsSmpFile.VerticalTagOffset] = 0x1A;
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.VerticalTagOffset + 1, 2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(XionicsSmpFile.VerticalResolutionOffset, 2), (ushort)Math.Clamp(file.VerticalResolution, 0, ushort.MaxValue));

    reversed.CopyTo(output, XionicsSmpFile.HeaderSize);
    return output;
  }
}
