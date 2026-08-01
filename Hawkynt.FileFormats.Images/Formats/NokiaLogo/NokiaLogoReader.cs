using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.NokiaLogo;

/// <summary>Reads Nokia Operator Logo files from bytes, streams, or file paths.</summary>
public static class NokiaLogoReader {

  public static NokiaLogoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaLogoFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static NokiaLogoFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NokiaLogoFile.HeaderSize
        || Encoding.ASCII.GetString(data[..NokiaLogoFile.Signature.Length]) != NokiaLogoFile.Signature)
      throw new InvalidDataException("Not a Nokia operator logo.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[NokiaLogoFile.WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[NokiaLogoFile.HeightOffset..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Nokia logo states no size: {width}x{height}.");

    var pixels = width * height;
    if (data.Length < NokiaLogoFile.HeaderSize + pixels)
      throw new InvalidDataException(
        $"{width}x{height} needs {NokiaLogoFile.HeaderSize + pixels} bytes; this file is {data.Length}.");

    // The body is text: anything that is not the letter zero counts as ink.
    var indices = new byte[pixels];
    for (var i = 0; i < pixels; ++i)
      indices[i] = (byte)(data[NokiaLogoFile.HeaderSize + i] == '0' ? 0 : 1);

    return new() { Width = width, Height = height, PixelData = indices };
  }

  public static NokiaLogoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
