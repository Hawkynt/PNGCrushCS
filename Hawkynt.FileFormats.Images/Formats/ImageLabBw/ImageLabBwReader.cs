using System;
using System.IO;

namespace FileFormat.ImageLabBw;

/// <summary>Reads ImageLab greyscale pictures from bytes, streams, or file paths.</summary>
public static class ImageLabBwReader {

  public static ImageLabBwFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Greyscale picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImageLabBwFile FromStream(Stream stream) {
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

  public static ImageLabBwFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ImageLabBwFile.HeaderSize + 1
        || !data[..ImageLabBwFile.Magic.Length].SequenceEqual(ImageLabBwFile.Magic))
      throw new InvalidDataException("Not an ImageLab picture: the signature is missing.");

    // Big-endian, unlike almost everything else here — the Falcon's processor is.
    var width = (data[6] << 8) | data[7];
    var height = (data[8] << 8) | data[9];
    if (width < 1 || height < 1 || width > ImageLabBwFile.MaxDimension || height > ImageLabBwFile.MaxDimension)
      throw new InvalidDataException($"Not an ImageLab picture: the header claims {width}x{height}.");

    var expected = ImageLabBwFile.HeaderSize + width * height;
    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} greyscale picture is {expected} bytes, got {data.Length}.");

    var pixels = new byte[width * height];
    data[ImageLabBwFile.HeaderSize..].CopyTo(pixels);

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  public static ImageLabBwFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
