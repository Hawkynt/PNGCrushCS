using System;

namespace FileFormat.PmgDesigner;

/// <summary>Assembles PMG Designer sheet bytes from a <see cref="PmgDesignerFile"/>.</summary>
/// <remarks>
/// The sheet is one array from its signature to its last shape, and the reader keeps it whole
/// because every area is found by counting from the header. So writing it is returning it, and the
/// assembling is done where the picture is turned into it.
/// </remarks>
public static class PmgDesignerWriter {

  public static byte[] ToBytes(PmgDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return (file.Data ?? [])[..];
  }
}
