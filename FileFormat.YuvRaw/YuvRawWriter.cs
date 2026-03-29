using System;

namespace FileFormat.YuvRaw;

/// <summary>Assembles raw YUV 4:2:0 planar bytes from a <see cref="YuvRawFile"/>.</summary>
public static class YuvRawWriter {

  public static byte[] ToBytes(YuvRawFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var ySize = file.Width * file.Height;
    var uvSize = (file.Width / 2) * (file.Height / 2);
    var result = new byte[ySize + uvSize * 2];

    file.YPlane.AsSpan(0, Math.Min(file.YPlane.Length, ySize)).CopyTo(result.AsSpan(0));
    file.UPlane.AsSpan(0, Math.Min(file.UPlane.Length, uvSize)).CopyTo(result.AsSpan(ySize));
    file.VPlane.AsSpan(0, Math.Min(file.VPlane.Length, uvSize)).CopyTo(result.AsSpan(ySize + uvSize));

    return result;
  }
}
