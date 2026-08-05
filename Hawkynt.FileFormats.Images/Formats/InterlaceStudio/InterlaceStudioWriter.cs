using System;

namespace FileFormat.InterlaceStudio;

/// <summary>Assembles Interlace Studio picture bytes.</summary>
public static class InterlaceStudioWriter {

  public static byte[] ToBytes(InterlaceStudioFile file) {
    ArgumentNullException.ThrowIfNull(file.FirstFrame);
    ArgumentNullException.ThrowIfNull(file.SecondFrame);

    var result = new byte[InterlaceStudioFile.MinimumFileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, InterlaceStudioFile.HeaderSize)).CopyTo(result);

    file.FirstFrame.AsSpan(0, Math.Min(file.FirstFrame.Length, InterlaceStudioFile.FrameSize))
      .CopyTo(result.AsSpan(InterlaceStudioFile.FirstFrameOffset));
    file.SecondFrame.AsSpan(0, Math.Min(file.SecondFrame.Length, InterlaceStudioFile.FrameSize))
      .CopyTo(result.AsSpan(InterlaceStudioFile.SecondFrameOffset));

    return result;
  }
}
