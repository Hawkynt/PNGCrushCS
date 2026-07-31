using System;
using System.Collections.Generic;

namespace FileFormat.LdPic;

/// <summary>Assembles LdPic picture bytes from an <see cref="LdPicFile"/>.</summary>
public static class LdPicWriter {

  /// <summary>Bits a stored value takes, which the file declares for itself.</summary>
  private const int _VALUE_BITS = 8;

  /// <summary>Bits a run length takes, likewise declared.</summary>
  private const int _COUNT_BITS = 8;

  /// <summary>
  /// Writes the screen with the interleaving stride set to one, so the bytes are visited in order.
  /// </summary>
  /// <remarks>
  /// The format's own stride exists to bring bytes eight scanlines apart next to each other, which
  /// helps a screen with large flat areas and hurts one without. A stride of one is what the runs
  /// see anyway once the picture is not flat, and it keeps the encoder from having to guess which
  /// stride would suit a picture it cannot see the shape of.
  /// </remarks>
  public static byte[] ToBytes(LdPicFile file) {
    var screen = file.Screen ?? [];
    var colors = file.LogicalColors ?? [];
    var body = new List<byte>();
    var emitted = 0;

    void Bit(int bit) {
      if (emitted % 8 == 0)
        body.Add(0);

      if (bit != 0)
        body[^1] |= (byte)(1 << (7 - emitted % 8));

      ++emitted;
    }

    // A field's bits go in from the bottom up, though the bits themselves come off the top of each
    // byte — so a field reads backwards relative to how it is stored.
    void Field(int value, int count) {
      for (var i = 0; i < count; ++i)
        Bit((value >> i) & 1);
    }

    Field(_VALUE_BITS, 8);
    Field(file.Mode, 8);

    // The sixteen logical colours, written from the last backwards, each naming one of the eight
    // the machine has.
    for (var i = 15; i >= 0; --i) {
      var entry = i * 3;
      // The machine's palette runs red, green, blue from the bottom bit up, which is the order a
      // colour's own channels are already in.
      var index = 0;
      if (entry + 2 < colors.Length)
        index = ((colors[entry] >= 128 ? 1 : 0) << 0)
                | ((colors[entry + 1] >= 128 ? 1 : 0) << 1)
                | ((colors[entry + 2] >= 128 ? 1 : 0) << 2);

      Field(index, 4);
    }

    Field(1, 8);
    Field(_COUNT_BITS, 8);

    for (var i = 0; i < screen.Length;) {
      var run = 1;
      while (run < (1 << _COUNT_BITS) - 1 && i + run < screen.Length && screen[i + run] == screen[i])
        ++run;

      // A run costs a bit, a length and a value; a single byte costs a bit and a value. So the run
      // form pays from two upwards, which is why there is no threshold here.
      if (run == 1)
        Bit(0);
      else {
        Bit(1);
        Field(run, _COUNT_BITS);
      }

      Field(screen[i], _VALUE_BITS);
      i += run;
    }

    while (emitted % 8 != 0)
      Bit(0);

    return body.ToArray();
  }
}
