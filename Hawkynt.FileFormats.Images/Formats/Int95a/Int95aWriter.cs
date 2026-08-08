using System;

namespace FileFormat.Int95a;

/// <summary>Assembles an INT95a picture: both frames, then the four registers they share.</summary>
public static class Int95aWriter {

  public static byte[] ToBytes(Int95aFile file) {
    var frame = Int95aFile.BytesPerRow * file.Height;
    var result = new byte[Int95aFile.FileSizeFor(file.Height)];

    (file.FirstFrame ?? []).AsSpan(0, Math.Min((file.FirstFrame ?? []).Length, frame)).CopyTo(result);
    (file.SecondFrame ?? []).AsSpan(0, Math.Min((file.SecondFrame ?? []).Length, frame)).CopyTo(result.AsSpan(frame));
    (file.Registers ?? []).AsSpan(0, Math.Min((file.Registers ?? []).Length, Int95aFile.RegisterCount))
      .CopyTo(result.AsSpan(frame * 2));

    return result;
  }
}
