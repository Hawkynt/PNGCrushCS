using System;
using System.IO;

namespace FileFormat.IcDraw;

/// <summary>Reads ICDRAW icons from bytes, streams, or file paths.</summary>
public static class IcDrawReader {

  public static IcDrawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICDRAW icon not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcDrawFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static IcDrawFile FromSpan(ReadOnlySpan<byte> data) {
    var variant = data.Length switch {
      IcDrawFile.SingleIconFileSize => IcDrawVariant.SingleIcon,
      IcDrawFile.IconGroupFileSize => IcDrawVariant.IconGroup,
      _ => throw new InvalidDataException(
        $"An ICDRAW icon is {IcDrawFile.SingleIconFileSize} or {IcDrawFile.IconGroupFileSize} bytes, got {data.Length}."),
    };

    var signature = variant == IcDrawVariant.SingleIcon ? IcDrawFile.SingleIconSignature : IcDrawFile.IconGroupSignature;
    if (!data[..signature.Length].SequenceEqual(signature))
      throw new InvalidDataException("Not an ICDRAW icon: the tag does not match the file size.");
    if (data[IcDrawFile.SizeOffset + 1] != IcDrawFile.IconSize || data[IcDrawFile.SizeOffset + 3] != IcDrawFile.IconSize)
      throw new InvalidDataException($"ICDRAW icons are {IcDrawFile.IconSize}x{IcDrawFile.IconSize}; this one is not.");

    var header = new byte[IcDrawFile.HeaderSize];
    data[..IcDrawFile.HeaderSize].CopyTo(header);

    var imageData = new byte[IcDrawFile.ImageDataSize];
    data.Slice(IcDrawFile.HeaderSize, IcDrawFile.ImageDataSize).CopyTo(imageData);

    var rest = data[(IcDrawFile.HeaderSize + IcDrawFile.ImageDataSize)..];
    return new() {
      Variant = variant,
      Header = header,
      ImageData = imageData,
      Mask = variant == IcDrawVariant.SingleIcon ? rest.ToArray() : [],
      AdditionalImages = variant == IcDrawVariant.IconGroup ? rest.ToArray() : [],
    };
  }

  public static IcDrawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
