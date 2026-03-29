using System;

namespace FileFormat.MobileFax;

/// <summary>Assembles MobileFax RFA file bytes from a MobileFaxFile.</summary>
public static class MobileFaxWriter {

  public static byte[] ToBytes(MobileFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[MobileFaxFile.HeaderSize + pixelDataSize];

    result[0] = MobileFaxFile.Magic[0];
    result[1] = MobileFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(MobileFaxFile.HeaderSize));

    return result;
  }
}
