using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Jpeg;

namespace FileFormat.DispThumbnail;

/// <summary>Reads Thumbnail files (.tnl) from bytes, streams, or file paths.</summary>
public static class DispThumbnailReader {

  /// <summary>The three bytes a JFIF opens with.</summary>
  private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

  public static DispThumbnailFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Thumbnail file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DispThumbnailFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static DispThumbnailFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DispThumbnailFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= DispThumbnailFile.PictureOffset)
      throw new InvalidDataException($"Data too small for a thumbnail (the picture begins at {DispThumbnailFile.PictureOffset} and the file has {data.Length} bytes).");

    if (!data[..DispThumbnailFile.Magic.Length].SequenceEqual(DispThumbnailFile.Magic))
      throw new InvalidDataException("Not a thumbnail: it does not open with DISPTNL.");

    var payload = data[DispThumbnailFile.PictureOffset..];

    if (data[DispThumbnailFile.Magic.Length] == DispThumbnailFile.JpegMarker) {
      if (payload.Length < JpegSignature.Length || !payload[..JpegSignature.Length].SequenceEqual(JpegSignature))
        throw new InvalidDataException($"A DISPTNL5 thumbnail carries a JPEG at {DispThumbnailFile.PictureOffset} and this one does not.");

      var embedded = payload.ToArray();
      var jpeg = JpegReader.FromBytes(embedded);
      return new() { Width = jpeg.Width, Height = jpeg.Height, Embedded = embedded };
    }

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[DispThumbnailFile.WidthAt..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[DispThumbnailFile.HeightAt..]);
    if (width is < 1 or > DispThumbnailFile.MaximumSide || height is < 1 or > DispThumbnailFile.MaximumSide)
      throw new InvalidDataException($"Invalid thumbnail dimensions: {width}x{height}.");

    var needed = (long)width * height;
    if (payload.Length < needed)
      throw new InvalidDataException($"A {width}x{height} thumbnail needs {needed + DispThumbnailFile.PictureOffset} bytes and the file has {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = payload[..(int)needed].ToArray(),
    };
  }
}
