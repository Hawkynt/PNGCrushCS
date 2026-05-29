using System;

namespace FileFormat.SpritePad;

/// <summary>Assembles SpritePad (.spd) file bytes from a SpritePadFile.</summary>
public static class SpritePadWriter {

  public static byte[] ToBytes(SpritePadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var isV2 = file.Version >= 2 && file.ExtraHeader.Length > 0;
    var headerSize = isV2 ? SpritePadFile.V2HeaderSize : SpritePadFile.V1HeaderSize;
    var result = new byte[headerSize + file.RawData.Length];

    result[0] = file.Version;
    result[1] = file.SpriteCount;
    result[2] = (byte)(file.IsMulticolor ? 1 : 0);

    if (isV2) {
      var copyLen = Math.Min(file.ExtraHeader.Length, SpritePadFile.V2HeaderSize - SpritePadFile.V1HeaderSize);
      file.ExtraHeader.AsSpan(0, copyLen).CopyTo(result.AsSpan(SpritePadFile.V1HeaderSize));
    }

    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(headerSize));

    return result;
  }
}
