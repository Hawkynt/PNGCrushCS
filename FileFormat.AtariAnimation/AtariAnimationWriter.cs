using System;

namespace FileFormat.AtariAnimation;

/// <summary>Assembles Atari Animation bytes by concatenating all frames.</summary>
public static class AtariAnimationWriter {

  public static byte[] ToBytes(AtariAnimationFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var totalSize = file.Frames.Length * AtariAnimationFile.FrameSize;
    var result = new byte[totalSize];

    for (var i = 0; i < file.Frames.Length; ++i) {
      var frame = file.Frames[i];
      frame.AsSpan(0, Math.Min(frame.Length, AtariAnimationFile.FrameSize)).CopyTo(result.AsSpan(i * AtariAnimationFile.FrameSize));
    }

    return result;
  }
}
