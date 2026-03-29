using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.AnimPainter;

/// <summary>Reads Anim Painter (animated C64 multicolor) files from bytes, streams, or file paths.</summary>
public static class AnimPainterReader {

  /// <summary>Minimum valid file size: 2-byte load address + at least 1 frame (10001 bytes).</summary>
  internal const int MinFileSize = AnimPainterFile.LoadAddressSize + AnimPainterFile.BytesPerFrame;

  public static AnimPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Anim Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AnimPainterFile FromStream(Stream stream) {
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

  public static AnimPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinFileSize)
      throw new InvalidDataException($"Data too small for Anim Painter file (got {data.Length} bytes, need at least {MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += AnimPainterFile.LoadAddressSize;

    // Read frames until end of data
    var frames = new List<AnimPainterFrame>();
    while (offset + AnimPainterFile.BytesPerFrame <= data.Length) {
      // Bitmap data (8000 bytes)
      var bitmapData = new byte[AnimPainterFile.BitmapDataSize];
      data.AsSpan(offset, AnimPainterFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
      offset += AnimPainterFile.BitmapDataSize;

      // Video matrix / screen RAM (1000 bytes)
      var videoMatrix = new byte[AnimPainterFile.VideoMatrixSize];
      data.AsSpan(offset, AnimPainterFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
      offset += AnimPainterFile.VideoMatrixSize;

      // Color RAM (1000 bytes)
      var colorRam = new byte[AnimPainterFile.ColorRamSize];
      data.AsSpan(offset, AnimPainterFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
      offset += AnimPainterFile.ColorRamSize;

      // Background color (1 byte)
      var backgroundColor = data[offset];
      offset += AnimPainterFile.BackgroundColorSize;

      frames.Add(new(bitmapData, videoMatrix, colorRam, backgroundColor));
    }

    if (frames.Count == 0)
      throw new InvalidDataException("Anim Painter file contains no complete frames.");

    return new() {
      LoadAddress = loadAddress,
      Frames = frames,
    };
  }
}
