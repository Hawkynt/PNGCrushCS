using System;

namespace FileFormat.NewsRoom;

/// <summary>Assembles NewsRoom NSR file bytes from a NewsRoomFile.</summary>
public static class NewsRoomWriter {

  public static byte[] ToBytes(NewsRoomFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[NewsRoomFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, NewsRoomFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
