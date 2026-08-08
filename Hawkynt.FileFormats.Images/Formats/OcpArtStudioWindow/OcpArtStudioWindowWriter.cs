using System;

namespace FileFormat.OcpArtStudioWindow;

/// <summary>Assembles Advanced OCP Art Studio window bytes from a file model.</summary>
/// <remarks>
/// The bitmap is written as it stands rather than packed. The packing is a run-length coding cut
/// into named blocks that a run may straddle, and a window is a clipping of a few kilobytes at most
/// — the reader accepts either, and every other tool decides which it has from the length just as it
/// does.
/// </remarks>
public static class OcpArtStudioWindowWriter {

  public static byte[] ToBytes(OcpArtStudioWindowFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bitmap = file.Bitmap ?? [];
    var length = file.Stride * file.Height;
    var data = new byte[length + OcpArtStudioWindowFile.TrailerLength];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, length)).CopyTo(data);

    // The size sits at the end because the program appended it once the picture was written, and it
    // counts screen positions rather than pixels — twice the width the picture is shown at.
    var stored = file.Width << 1;
    data[length + 1] = (byte)stored;
    data[length + 2] = (byte)(stored >> 8);
    data[length + 3] = (byte)file.Height;

    return data;
  }
}
