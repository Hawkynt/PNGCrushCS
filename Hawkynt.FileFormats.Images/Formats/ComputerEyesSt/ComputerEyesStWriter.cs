using System;

namespace FileFormat.ComputerEyesSt;

/// <summary>Assembles a ComputerEyes ST capture from a <see cref="ComputerEyesStFile"/>.</summary>
public static class ComputerEyesStWriter {

  /// <summary>Writes the capture, whose mode byte the reader takes the file's length from.</summary>
  public static byte[] ToBytes(ComputerEyesStFile file) {
    var data = (byte[])(file.Data ?? []).Clone();
    if (data.Length > 5)
      data[5] = file.Kind switch {
        ComputerEyesStKind.Color => 0,
        ComputerEyesStKind.HighResolutionColor => 1,
        _ => 2,
      };

    return data;
  }
}
