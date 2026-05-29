using System;
using System.IO;

namespace FileFormat.NewsRoom;

/// <summary>Reads NewsRoom NSR files from bytes, streams, or file paths.</summary>
public static class NewsRoomReader {

  public static NewsRoomFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NewsRoom file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NewsRoomFile FromStream(Stream stream) {
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

  public static NewsRoomFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != NewsRoomFile.ExpectedFileSize)
      throw new InvalidDataException($"NewsRoom file must be exactly {NewsRoomFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[NewsRoomFile.ExpectedFileSize];
    data.Slice(0, NewsRoomFile.ExpectedFileSize).CopyTo(pixelData);

    return new() {
      PixelData = pixelData,
    };
    }

  public static NewsRoomFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != NewsRoomFile.ExpectedFileSize)
      throw new InvalidDataException($"NewsRoom file must be exactly {NewsRoomFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[NewsRoomFile.ExpectedFileSize];
    data.AsSpan(0, NewsRoomFile.ExpectedFileSize).CopyTo(pixelData);

    return new() {
      PixelData = pixelData,
    };
  }
}
