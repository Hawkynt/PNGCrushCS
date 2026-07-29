using System;

namespace FileFormat.FloorDesigner;

/// <summary>Assembles Atari 8-bit Floor Designer (.fge) screens. bytes.</summary>
public static class FloorDesignerWriter {

  public static byte[] ToBytes(FloorDesignerFile file) {
    var result = new byte[FloorDesignerFile.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, FloorDesignerFile.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, FloorDesignerFile.ScreenDataSize))
      .CopyTo(result.AsSpan(FloorDesignerFile.HeaderSize));

    return result;
  }
}
