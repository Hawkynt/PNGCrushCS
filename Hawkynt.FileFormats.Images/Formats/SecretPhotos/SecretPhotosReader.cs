using System;
using System.IO;
using FileFormat.Jpeg;

namespace FileFormat.SecretPhotos;

/// <summary>Reads SecretPhotos puzzles (.xp0) from bytes, streams, or file paths.</summary>
public static class SecretPhotosReader {

  /// <summary>The three bytes a JFIF opens with.</summary>
  private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

  public static SecretPhotosFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SecretPhotos puzzle not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SecretPhotosFile FromStream(Stream stream) {
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

  public static SecretPhotosFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SecretPhotosFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= SecretPhotosFile.PictureOffset + JpegSignature.Length)
      throw new InvalidDataException($"Data too small for a SecretPhotos puzzle (the picture begins at {SecretPhotosFile.PictureOffset} and the file has {data.Length} bytes).");

    if (!data[..SecretPhotosFile.Magic.Length].SequenceEqual(SecretPhotosFile.Magic))
      throw new InvalidDataException("Not a SecretPhotos puzzle: it does not open with 00 00 00 01.");

    var payload = data[SecretPhotosFile.PictureOffset..];
    if (!payload[..JpegSignature.Length].SequenceEqual(JpegSignature))
      throw new InvalidDataException($"A SecretPhotos puzzle carries a JPEG at {SecretPhotosFile.PictureOffset} and this one does not.");

    var embedded = payload.ToArray();
    var jpeg = JpegReader.FromBytes(embedded);

    return new() {
      Width = jpeg.Width,
      Height = jpeg.Height,
      Embedded = embedded,
    };
  }
}
