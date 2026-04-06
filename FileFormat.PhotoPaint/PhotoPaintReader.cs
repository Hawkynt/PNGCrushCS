using System;
using System.IO;

namespace FileFormat.PhotoPaint;

/// <summary>Reads Corel Photo-Paint CPT files from bytes, streams, or file paths.</summary>
public static class PhotoPaintReader {

  public static PhotoPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoPaintFile FromStream(Stream stream) {
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

  public static PhotoPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PhotoPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PhotoPaintFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid CPT file (need at least {PhotoPaintFile.HeaderSize} bytes, got {data.Length}).");

    if (data[0] != PhotoPaintFile.Magic[0] || data[1] != PhotoPaintFile.Magic[1] || data[2] != PhotoPaintFile.Magic[2] || data[3] != PhotoPaintFile.Magic[3])
      throw new InvalidDataException("Invalid CPT magic bytes (expected 'CPT\\0').");

    var width = (int)(data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24));
    var height = (int)(data[12] | (data[13] << 8) | (data[14] << 16) | (data[15] << 24));

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid CPT image dimensions: {width}x{height}.");

    var expectedPixelBytes = width * height * 3;
    var pixelData = new byte[expectedPixelBytes];
    var available = Math.Min(data.Length - PhotoPaintFile.HeaderSize, expectedPixelBytes);
    data.AsSpan(PhotoPaintFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new PhotoPaintFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
