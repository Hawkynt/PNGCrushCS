using System;

namespace FileFormat.InShape;

/// <summary>Assembles InShape bytes from an <see cref="InShapeFile"/>.</summary>
/// <remarks>
/// The header and the pixels are one array here, as they are in the file and as the reader keeps
/// them, so writing is a matter of laying the header over the front of it rather than joining two
/// pieces that were never apart.
/// </remarks>
public static class InShapeWriter {

  public static byte[] ToBytes(InShapeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var source = file.Data ?? [];
    var data = new byte[Math.Max(source.Length, InShapeFile.PixelsOffset)];
    source.CopyTo(data, 0);

    for (var i = 0; i < InShapeFile.Signature.Length; ++i)
      data[i] = (byte)InShapeFile.Signature[i];

    data[8] = 0;
    data[9] = file.Mode;
    data[12] = (byte)(file.Width >> 8);
    data[13] = (byte)file.Width;
    data[14] = (byte)(file.Height >> 8);
    data[15] = (byte)file.Height;

    return data;
  }
}
