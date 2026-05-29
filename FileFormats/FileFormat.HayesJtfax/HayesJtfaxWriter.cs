using System;

namespace FileFormat.HayesJtfax;

/// <summary>Assembles Hayes JT Fax file bytes from a HayesJtfaxFile.</summary>
public static class HayesJtfaxWriter {

  public static byte[] ToBytes(HayesJtfaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[HayesJtfaxFile.HeaderSize + pixelDataSize];

    result[0] = HayesJtfaxFile.Magic[0];
    result[1] = HayesJtfaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    // reserved bytes at offset 8-9 stay zero

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(HayesJtfaxFile.HeaderSize));

    return result;
  }
}
