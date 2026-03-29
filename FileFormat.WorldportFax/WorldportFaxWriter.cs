using System;

namespace FileFormat.WorldportFax;

/// <summary>Assembles WorldportFax WPF file bytes from a WorldportFaxFile.</summary>
public static class WorldportFaxWriter {

  public static byte[] ToBytes(WorldportFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[WorldportFaxFile.HeaderSize + pixelDataSize];

    result[0] = WorldportFaxFile.Magic[0];
    result[1] = WorldportFaxFile.Magic[1];
    result[2] = WorldportFaxFile.Magic[2];
    result[3] = WorldportFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(WorldportFaxFile.HeaderSize));

    return result;
  }
}
