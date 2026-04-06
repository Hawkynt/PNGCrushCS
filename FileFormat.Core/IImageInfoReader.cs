using System;

namespace FileFormat.Core;

/// <summary>Reads image metadata from a header without decoding pixel data. Implement this for fast previews and format info display.</summary>
public interface IImageInfoReader<TSelf> where TSelf : IImageInfoReader<TSelf> {

  /// <summary>Extracts image metadata from raw header bytes. Only reads the minimum bytes needed — does NOT decode pixel data. Returns <c>null</c> if the data is too small or invalid.</summary>
  static abstract ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header);
}
