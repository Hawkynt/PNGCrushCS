namespace FileFormat.CharPad;

/// <summary>Assembles a CharPad project from a <see cref="CharPadFile"/>.</summary>
public static class CharPadWriter {

  /// <summary>
  /// Writes the project, which is already whole because its three sections are addressed by the
  /// counts in its header.
  /// </summary>
  public static byte[] ToBytes(CharPadFile file) => (byte[])(file.Data ?? []).Clone();
}
