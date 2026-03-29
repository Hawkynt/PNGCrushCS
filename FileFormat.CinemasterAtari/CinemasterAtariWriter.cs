using System;

namespace FileFormat.CinemasterAtari;

/// <summary>Assembles Cinemaster Atari bytes: 2-byte LE frame count header + concatenated frames.</summary>
public static class CinemasterAtariWriter {

  public static byte[] ToBytes(CinemasterAtariFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var totalSize = CinemasterAtariFile.HeaderSize + file.Frames.Length * CinemasterAtariFile.FrameSize;
    var result = new byte[totalSize];

    // Write 2-byte LE frame count
    var frameCount = (ushort)file.Frames.Length;
    result[0] = (byte)(frameCount & 0xFF);
    result[1] = (byte)((frameCount >> 8) & 0xFF);

    // Write frames
    for (var i = 0; i < file.Frames.Length; ++i) {
      var frame = file.Frames[i];
      frame.AsSpan(0, Math.Min(frame.Length, CinemasterAtariFile.FrameSize)).CopyTo(result.AsSpan(CinemasterAtariFile.HeaderSize + i * CinemasterAtariFile.FrameSize));
    }

    return result;
  }
}
