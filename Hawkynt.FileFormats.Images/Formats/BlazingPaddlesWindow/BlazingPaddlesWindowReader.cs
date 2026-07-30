using System;
using System.IO;

namespace FileFormat.BlazingPaddlesWindow;

/// <summary>Reads Blazing Paddles windows from bytes, streams, or file paths.</summary>
public static class BlazingPaddlesWindowReader {

  public static BlazingPaddlesWindowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Window not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BlazingPaddlesWindowFile FromStream(Stream stream) {
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

  public static BlazingPaddlesWindowFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != BlazingPaddlesWindowFile.FileSize)
      throw new InvalidDataException($"A window is {BlazingPaddlesWindowFile.FileSize} bytes, got {data.Length}.");

    // The stored width is one less than the real one, so that 256 logical pixels still fit a byte.
    var logicalWidth = data[0] + 1;
    var stride = (logicalWidth + 3) >> 2;
    var height = data[1];

    if (stride > BlazingPaddlesWindowFile.MaxStride || height == 0 || height > BlazingPaddlesWindowFile.MaxHeight
        || stride * height > BlazingPaddlesWindowFile.FileSize - BlazingPaddlesWindowFile.BitmapOffset)
      throw new InvalidDataException($"Not a window: {logicalWidth}x{height} does not fit the buffer.");

    return new() { Data = data.ToArray(), LogicalWidth = logicalWidth, Height = height };
  }

  public static BlazingPaddlesWindowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
