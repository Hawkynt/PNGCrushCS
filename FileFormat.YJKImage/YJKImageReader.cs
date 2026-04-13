using System;
using System.IO;

namespace FileFormat.YJKImage;

/// <summary>Reads YJK image files from bytes, streams, or file paths.</summary>
public static class YJKImageReader {

  public static YJKImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("YJK image file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static YJKImageFile FromStream(Stream stream) {
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

  public static YJKImageFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != YJKImageFile.ExpectedFileSize)
      throw new InvalidDataException($"YJK image file must be exactly {YJKImageFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[YJKImageFile.ExpectedFileSize];
    data.Slice(0, YJKImageFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
    }

  public static YJKImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != YJKImageFile.ExpectedFileSize)
      throw new InvalidDataException($"YJK image file must be exactly {YJKImageFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[YJKImageFile.ExpectedFileSize];
    data.AsSpan(0, YJKImageFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
  }
}
