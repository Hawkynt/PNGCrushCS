using System;
using System.Text;

namespace FileFormat.ZxPaintyOne;

/// <summary>Assembles ZXpaintyONE picture text from a <see cref="ZxPaintyOneFile"/>.</summary>
public static class ZxPaintyOneWriter {

  /// <summary>Writes the screen as hexadecimal text, which is the whole of the format.</summary>
  public static byte[] ToBytes(ZxPaintyOneFile file) {
    var screen = file.Screen ?? [];
    var text = new StringBuilder(screen.Length * 2);

    foreach (var code in screen)
      text.Append($"{code:X2}");

    return Encoding.ASCII.GetBytes(text.ToString());
  }
}
