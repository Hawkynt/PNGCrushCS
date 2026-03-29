using System;

namespace FileFormat.Interlace8;

/// <summary>Assembles Atari Interlace Mode file bytes from an <see cref="Interlace8File"/>.</summary>
public static class Interlace8Writer {

  public static byte[] ToBytes(Interlace8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Interlace8File.ExpectedFileSize];
    file.Frame1Data.AsSpan(0, Math.Min(file.Frame1Data.Length, Interlace8File.FrameSize)).CopyTo(result.AsSpan(0));
    file.Frame2Data.AsSpan(0, Math.Min(file.Frame2Data.Length, Interlace8File.FrameSize)).CopyTo(result.AsSpan(Interlace8File.FrameSize));
    return result;
  }
}
