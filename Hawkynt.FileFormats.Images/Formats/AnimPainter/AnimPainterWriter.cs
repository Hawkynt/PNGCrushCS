using System;

namespace FileFormat.AnimPainter;

/// <summary>Assembles Anim Painter (animated C64 multicolor) file bytes from an AnimPainterFile.</summary>
public static class AnimPainterWriter {

  public static byte[] ToBytes(AnimPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var totalSize = AnimPainterFile.LoadAddressSize + file.Frames.Count * AnimPainterFile.BytesPerFrame;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += AnimPainterFile.LoadAddressSize;

    // Write each frame
    foreach (var frame in file.Frames) {
      // Bitmap data (8000 bytes)
      frame.BitmapData.AsSpan(0, AnimPainterFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
      offset += AnimPainterFile.BitmapDataSize;

      // Video matrix / screen RAM (1000 bytes)
      frame.VideoMatrix.AsSpan(0, AnimPainterFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
      offset += AnimPainterFile.VideoMatrixSize;

      // Color RAM (1000 bytes)
      frame.ColorRam.AsSpan(0, AnimPainterFile.ColorRamSize).CopyTo(result.AsSpan(offset));
      offset += AnimPainterFile.ColorRamSize;

      // Background color (1 byte)
      result[offset] = frame.BackgroundColor;
      offset += AnimPainterFile.BackgroundColorSize;
    }

    return result;
  }
}
