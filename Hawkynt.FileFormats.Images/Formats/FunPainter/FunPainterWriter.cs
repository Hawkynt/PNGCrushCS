using System;
using System.IO;

namespace FileFormat.FunPainter;

/// <summary>Writes a Fun Painter II picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole because every area of it is at an absolute offset, so there is
/// nothing to reassemble here beyond insisting on the length those offsets assume, and packing the
/// payload again where the picture came packed.
/// </remarks>
public static class FunPainterWriter {

  public static byte[] ToBytes(FunPainterFile file) {
    var data = file.Data;
    if (data == null || data.Length < FunPainterFile.FileSize)
      throw new InvalidDataException($"A Fun Painter picture is {FunPainterFile.FileSize} bytes.");

    var whole = data.AsSpan(0, FunPainterFile.FileSize);
    var body = whole[FunPainterFile.PayloadOffset..];
    var escape = FunPainterRle.ChooseEscape(body);
    var packed = file.Packed ? FunPainterRle.Pack(body, escape) : [];

    // Packing a picture with no runs in it makes the file bigger, and the format can say either.
    if (!file.Packed || packed.Length >= body.Length) {
      var plain = whole.ToArray();
      plain[FunPainterFile.PackedFlagOffset] = 0;
      return plain;
    }

    var result = new byte[FunPainterFile.PayloadOffset + packed.Length];
    whole[..FunPainterFile.PayloadOffset].CopyTo(result);
    result[FunPainterFile.PackedFlagOffset] = 1;
    result[FunPainterFile.EscapeOffset] = escape;
    packed.CopyTo(result.AsSpan(FunPainterFile.PayloadOffset));

    return result;
  }
}
