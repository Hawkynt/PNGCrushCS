using System;

namespace FileFormat.SeattleFilmWorks;

/// <summary>Assembles Seattle Film Works (SFW) file bytes from an in-memory representation.</summary>
public static class SeattleFilmWorksWriter {

  public static byte[] ToBytes(SeattleFilmWorksFile file) {
    ArgumentNullException.ThrowIfNull(file);
    // When the file was produced from raw pixels (no JPEG carried), emit a
    // round-trippable payload that the matching reader can parse: SFW magic + a
    // minimal SOI/EOI + a custom APP15 segment carrying Width, Height and raw RGB
    // pixel bytes. Otherwise emit the carried JPEG unchanged after the magic.
    if (file.JpegData is { Length: > 0 })
      return Assemble(file.JpegData);

    return _BuildRawPayload(file.Width, file.Height, file.PixelData ?? []);
  }

  /// <summary>Prepends the SFW94A magic header to the given JPEG data.</summary>
  internal static byte[] Assemble(byte[] jpegData) {
    ArgumentNullException.ThrowIfNull(jpegData);

    var result = new byte[SeattleFilmWorksFile.MAGIC_LENGTH + jpegData.Length];
    SeattleFilmWorksFile.SfwMagic.AsSpan().CopyTo(result);
    jpegData.AsSpan().CopyTo(result.AsSpan(SeattleFilmWorksFile.MAGIC_LENGTH));
    return result;
  }

  // Layout: magic(6) | SOI(2) | APP15 marker(2) | len_be(2) | W(2,LE) | H(2,LE) | RGB...(W*H*3) | EOI(2)
  // The APP15 length field counts itself + payload (W,H,RGB) but not the FFEF marker.
  private static byte[] _BuildRawPayload(int width, int height, byte[] rgb) {
    var w = (ushort)width;
    var h = (ushort)height;
    var pixels = rgb.Length;
    // APP15 length: 2 (length) + 2 (W) + 2 (H) + pixels
    var appLen = 2 + 2 + 2 + pixels;
    var total = SeattleFilmWorksFile.MAGIC_LENGTH + 2 /*SOI*/ + 2 /*APP15*/ + appLen + 2 /*EOI*/;
    var result = new byte[total];

    var pos = 0;
    SeattleFilmWorksFile.SfwMagic.AsSpan().CopyTo(result.AsSpan(pos));
    pos += SeattleFilmWorksFile.MAGIC_LENGTH;
    result[pos++] = 0xFF; result[pos++] = 0xD8; // SOI
    result[pos++] = 0xFF; result[pos++] = 0xEF; // APP15
    // length is big-endian per JPEG convention
    result[pos++] = (byte)((appLen >> 8) & 0xFF);
    result[pos++] = (byte)(appLen & 0xFF);
    result[pos++] = (byte)(w & 0xFF);
    result[pos++] = (byte)((w >> 8) & 0xFF);
    result[pos++] = (byte)(h & 0xFF);
    result[pos++] = (byte)((h >> 8) & 0xFF);
    if (pixels > 0) {
      rgb.AsSpan().CopyTo(result.AsSpan(pos));
      pos += pixels;
    }
    result[pos++] = 0xFF; result[pos++] = 0xD9; // EOI
    return result;
  }
}
