using System;

namespace FileFormat.SeattleFilmWorks;

/// <summary>Assembles Seattle Film Works (SFW) file bytes from an in-memory representation.</summary>
public static class SeattleFilmWorksWriter {

  public static byte[] ToBytes(SeattleFilmWorksFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.JpegData);
  }

  /// <summary>Prepends the SFW94A magic header to the given JPEG data.</summary>
  internal static byte[] Assemble(byte[] jpegData) {
    ArgumentNullException.ThrowIfNull(jpegData);

    var result = new byte[SeattleFilmWorksFile.MAGIC_LENGTH + jpegData.Length];
    SeattleFilmWorksFile.SfwMagic.AsSpan().CopyTo(result);
    jpegData.AsSpan().CopyTo(result.AsSpan(SeattleFilmWorksFile.MAGIC_LENGTH));
    return result;
  }
}
