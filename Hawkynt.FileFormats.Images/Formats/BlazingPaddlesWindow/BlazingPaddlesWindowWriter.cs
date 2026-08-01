using System;

namespace FileFormat.BlazingPaddlesWindow;

/// <summary>Assembles a Blazing Paddles window from a <see cref="BlazingPaddlesWindowFile"/>.</summary>
public static class BlazingPaddlesWindowWriter {

  /// <summary>Writes the fixed-size buffer the program always saved, whatever the window's size.</summary>
  public static byte[] ToBytes(BlazingPaddlesWindowFile file) {
    var data = file.Data ?? [];
    var result = new byte[BlazingPaddlesWindowFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    // Stored one less than the real width, so that 256 logical pixels still fit a byte.
    result[0] = (byte)(file.LogicalWidth - 1);
    result[1] = (byte)file.Height;

    return result;
  }
}
