using System;
using System.IO;

namespace FileFormat.ZxAttributesGigascreen;

/// <summary>Reads ZX Spectrum Attributes Gigascreen (.hlr) images from bytes, streams, or file paths.</summary>
public static class ZxAttributesGigascreenReader {

  public static ZxAttributesGigascreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Attributes Gigascreen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxAttributesGigascreenFile FromStream(Stream stream) {
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

  public static ZxAttributesGigascreenFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ZxAttributesGigascreenFile.FileSize)
      throw new InvalidDataException(
        $"An Attributes Gigascreen file is exactly {ZxAttributesGigascreenFile.FileSize} bytes, got {data.Length}.");

    if (!data[..ZxAttributesGigascreenFile.LoaderSignature.Length].SequenceEqual(ZxAttributesGigascreenFile.LoaderSignature))
      throw new InvalidDataException("Not an Attributes Gigascreen file: the loader stub does not match.");

    var dither = new byte[8];
    data.Slice(ZxAttributesGigascreenFile.DitherOffset, dither.Length).CopyTo(dither);

    var first = new byte[ZxAttributesGigascreenFile.AttributesSize];
    data.Slice(ZxAttributesGigascreenFile.FirstAttributesOffset, first.Length).CopyTo(first);

    var second = new byte[ZxAttributesGigascreenFile.AttributesSize];
    data.Slice(ZxAttributesGigascreenFile.SecondAttributesOffset, second.Length).CopyTo(second);

    return new() { Dither = dither, FirstAttributes = first, SecondAttributes = second };
  }

  public static ZxAttributesGigascreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
