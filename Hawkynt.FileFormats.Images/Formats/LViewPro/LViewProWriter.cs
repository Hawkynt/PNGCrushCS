using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.LViewPro;

/// <summary>Assembles an LView Pro image file: the header, then the JPEG.</summary>
public static class LViewProWriter {

  public static byte[] ToBytes(LViewProFile file) {
    var embedded = file.Embedded ?? [];
    var result = new byte[LViewProFile.DefaultPictureOffset + embedded.Length];

    LViewProFile.Magic.CopyTo(result);
    Encoding.ASCII.GetBytes(LViewProFile.Title).CopyTo(result, LViewProFile.TitleAt);
    result[LViewProFile.DepthAt] = (byte)(file.Depth < 1 ? 8 : file.Depth);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(LViewProFile.WidthAt), file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(LViewProFile.HeightAt), file.Height);

    embedded.CopyTo(result.AsSpan(LViewProFile.DefaultPictureOffset));

    return result;
  }
}
