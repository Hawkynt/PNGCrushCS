using System;

namespace FileFormat.Ccitt;

/// <summary>Encodes 1bpp pixel data to CCITT-compressed bytes.</summary>
public static class CcittWriter {

  /// <summary>Length of the round-trip header prepended by <see cref="ToBytes"/>.</summary>
  internal const int HeaderSize = 8;

  /// <summary>4-byte magic ("CCIT") used to recognise self-describing payloads.</summary>
  internal static readonly byte[] Magic = [(byte)'C', (byte)'C', (byte)'I', (byte)'T'];

  public static byte[] ToBytes(CcittFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var payload = file.Format switch {
      CcittFormat.Group3_1D => CcittG3Encoder.Encode(file.PixelData, file.Width, file.Height),
      CcittFormat.Group4 => CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height),
      _ => throw new NotSupportedException($"CCITT format {file.Format} is not supported.")
    };

    // Prepend a small header so the matching reader (which has no external metadata)
    // can recover Width / Height / Format. Layout:
    //   magic[4]="CCIT" | W(LE u16) | H(LE u16) | fmt(u8) | reserved(u8) | payload...
    // Note: 4 + 2 + 2 + 1 + 1 = 10 bytes? Actually HeaderSize=8 above — keep it at 8
    // by using only "CCIT" + W(2) + H(2) and store the format byte at the end of the
    // header via the high-bit of the magic — instead, lay out as:
    //   "CCIT" | W(2 LE) | H(2 LE) | payload ...
    // The format flag is encoded by repurposing the lowest bit of the high byte of H.
    var result = new byte[HeaderSize + payload.Length];
    Magic.AsSpan().CopyTo(result.AsSpan(0));
    var w = (ushort)file.Width;
    var h = (ushort)file.Height;
    result[4] = (byte)(w & 0xFF);
    result[5] = (byte)((w >> 8) & 0xFF);
    result[6] = (byte)(h & 0xFF);
    // Store CcittFormat in the high byte of H: bit 7 = format flag (0 = G3, 1 = G4),
    // remaining bits hold the high byte of H (capped at 0x7F).
    var hHi = (byte)((h >> 8) & 0x7F);
    if (file.Format == CcittFormat.Group4) hHi |= 0x80;
    result[7] = hHi;
    payload.AsSpan().CopyTo(result.AsSpan(HeaderSize));
    return result;
  }
}
