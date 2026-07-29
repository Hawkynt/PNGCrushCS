using System;

namespace FileFormat.IcDraw;

/// <summary>Assembles ICDRAW icon bytes from an <see cref="IcDrawFile"/>.</summary>
public static class IcDrawWriter {

  public static byte[] ToBytes(IcDrawFile file) {
    var single = file.Variant == IcDrawVariant.SingleIcon;
    var result = new byte[single ? IcDrawFile.SingleIconFileSize : IcDrawFile.IconGroupFileSize];

    _Copy(file.Header, result, 0, IcDrawFile.HeaderSize);
    (single ? IcDrawFile.SingleIconSignature : IcDrawFile.IconGroupSignature).CopyTo(result);
    result[IcDrawFile.SizeOffset + 1] = IcDrawFile.IconSize;
    result[IcDrawFile.SizeOffset + 3] = IcDrawFile.IconSize;

    _Copy(file.ImageData, result, IcDrawFile.HeaderSize, IcDrawFile.ImageDataSize);

    var tailOffset = IcDrawFile.HeaderSize + IcDrawFile.ImageDataSize;
    if (single)
      _Copy(file.Mask, result, tailOffset, IcDrawFile.MaskDataSize);
    else
      _Copy(file.AdditionalImages, result, tailOffset, IcDrawFile.ImageDataSize * (IcDrawFile.GroupImageCount - 1));

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
