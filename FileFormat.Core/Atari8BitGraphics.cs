using System;

namespace FileFormat.Core;

/// <summary>
/// Primitives shared by the Atari 8-bit picture formats: the GTIA colour palette and the ANTIC
/// mode D ("Graphics 7") bitmap layout.
/// </summary>
/// <remarks>
/// Dozens of Atari 8-bit formats are a Graphics 7 bitmap plus a handful of colour registers, so
/// the packing and the palette live here rather than being reimplemented per format.
/// </remarks>
public static class Atari8BitGraphics {

  /// <summary>Logical pixels across a Graphics 7 line. Each is displayed two screen pixels wide.</summary>
  public const int Gr7Width = 160;

  /// <summary>Bytes per Graphics 7 scanline: 160 pixels at 2 bits each.</summary>
  public const int Gr7BytesPerRow = Gr7Width / 4;

  /// <summary>Colour registers a Graphics 7 screen carries: PF0, PF1, PF2, PF3 and BAK.</summary>
  public const int ColorRegisterCount = 5;

  /// <summary>Index of the background register within a <see cref="ColorRegisterCount"/> block.</summary>
  public const int BackgroundRegisterIndex = 4;

  /// <summary>
  /// Maps a Graphics 7 pixel value to the colour register that draws it. Value 0 comes from the
  /// background register; 1, 2 and 3 come from PF0, PF1 and PF2. PF3 is unused in this mode.
  /// </summary>
  public static int RegisterForPixel(int pixel) => pixel == 0 ? BackgroundRegisterIndex : pixel - 1;

  /// <summary>Unpacks a Graphics 7 bitmap into one byte per logical pixel (values 0..3).</summary>
  /// <param name="data">Source bytes.</param>
  /// <param name="offset">Offset of the bitmap.</param>
  /// <param name="rows">Number of scanlines to unpack.</param>
  public static byte[] UnpackGr7(ReadOnlySpan<byte> data, int offset, int rows) {
    var pixels = new byte[Gr7Width * rows];
    for (var y = 0; y < rows; ++y) {
      var rowOffset = offset + y * Gr7BytesPerRow;
      for (var x = 0; x < Gr7Width; ++x) {
        var index = rowOffset + (x >> 2);
        if (index >= data.Length)
          break;

        // Four pixels per byte, most significant pair first.
        var shift = 6 - ((x & 3) << 1);
        pixels[y * Gr7Width + x] = (byte)((data[index] >> shift) & 3);
      }
    }

    return pixels;
  }

  /// <summary>Packs one byte per logical pixel (values 0..3) into the Graphics 7 bitmap layout.</summary>
  public static byte[] PackGr7(ReadOnlySpan<byte> pixels, int rows) {
    var data = new byte[Gr7BytesPerRow * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < Gr7Width; ++x) {
      var source = y * Gr7Width + x;
      if (source >= pixels.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      data[y * Gr7BytesPerRow + (x >> 2)] |= (byte)((pixels[source] & 3) << shift);
    }

    return data;
  }

  /// <summary>Colour registers the GTIA offers: the border, three players and the playfield.</summary>
  public const int RegisterCount = 9;

  /// <summary>Entries a Graphics 10 pixel can select; nine registers fill all sixteen.</summary>
  public const int Gr10EntryCount = 16;

  /// <summary>
  /// Expands the nine GTIA colour registers into the sixteen entries a Graphics 10 pixel indexes.
  /// </summary>
  /// <remarks>
  /// A Graphics 10 pixel carries four bits but the chip has only nine registers to offer, so seven
  /// of the sixteen entries are aliases: the background repeats across four of them and the four
  /// playfield registers each appear a second time near the top. Treating the missing entries as
  /// black instead — the obvious reading of a four-bit index against a nine-entry table — turns
  /// every pixel that lands on an alias into a hole in the picture.
  /// </remarks>
  public static byte[] ExpandGr10Registers(ReadOnlySpan<byte> registers) {
    var entries = new byte[Gr10EntryCount];
    for (var register = 0; register < RegisterCount && register < registers.Length; ++register) {
      // The low bit of a colour register does not reach the screen.
      var value = (byte)(registers[register] & 254);
      entries[register] = value;

      switch (register) {
        case >= 4 and <= 7:
          entries[8 + register] = value;
          break;
        case 8:
          entries[9] = entries[10] = entries[11] = value;
          break;
      }
    }

    return entries;
  }

  /// <summary>Logical pixels across an ANTIC mode E ("Graphics 15") line.</summary>
  public const int Gr15Width = 160;

  /// <summary>Bytes per Graphics 15 scanline: 160 pixels at 2 bits each.</summary>
  public const int Gr15BytesPerRow = Gr15Width / 4;

  /// <summary>Colour registers a Graphics 15 scanline draws from: PF0, PF1, PF2 and the background.</summary>
  public const int Gr15RegisterCount = 4;

  /// <summary>
  /// The register a Graphics 15 pixel draws from, as an index into a PF0-PF1-PF2-background block.
  /// </summary>
  /// <remarks>
  /// Pixel value 0 is the background and the other three are the playfield registers in order, so
  /// the mapping is a rotation rather than the identity — reading it as one turns every picture's
  /// colours inside out.
  /// </remarks>
  public static int RegisterForGr15Pixel(int pixel) => pixel == 0 ? Gr15RegisterCount - 1 : pixel - 1;

  /// <summary>Unpacks one Graphics 15 scanline into one byte per logical pixel (values 0..3).</summary>
  public static void UnpackGr15Row(ReadOnlySpan<byte> data, int offset, Span<byte> pixels) {
    for (var x = 0; x < Gr15Width; ++x) {
      var index = offset + (x >> 2);
      // Four pixels per byte, most significant pair leftmost.
      pixels[x] = index < data.Length ? (byte)((data[index] >> (6 - ((x & 3) << 1))) & 3) : (byte)0;
    }
  }

  /// <summary>
  /// Renders a Graphics 15 bitmap of any size to RGB, given the four registers it draws from.
  /// </summary>
  /// <param name="registers">Background, PF0, PF1 and PF2, in that order.</param>
  /// <param name="width">Screen pixels across. Each logical pixel is drawn two of them wide.</param>
  /// <remarks>
  /// The mode is the same everywhere it appears, but the frame size is not: editors that interlace
  /// two Graphics 15 screens choose their own width and height, so this works in screen pixels and
  /// takes the row stride rather than assuming the forty bytes a full-width screen uses.
  /// </remarks>
  public static byte[] DecodeGr15Frame(
    ReadOnlySpan<byte> data, int offset, int stride, int width, int height, ReadOnlySpan<byte> registers) {
    var gtia = Palette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var rowOffset = offset + y * stride;
      for (var x = 0; x < width; ++x) {
        var index = rowOffset + (x >> 3);
        // Two bits per logical pixel, four logical pixels to a byte, each drawn two pixels wide.
        var pixel = index < data.Length ? (data[index] >> (~x & 6)) & 3 : 0;
        var color = pixel < registers.Length ? registers[pixel] & 254 : 0;

        var entry = color * 3;
        var target = (y * width + x) * 3;
        rgb[target] = gtia[entry];
        rgb[target + 1] = gtia[entry + 1];
        rgb[target + 2] = gtia[entry + 2];
      }
    }

    return rgb;
  }

  /// <summary>
  /// Renders a Graphics 9 bitmap of any size to RGB: one luminance per pixel against a fixed hue.
  /// </summary>
  /// <param name="background">
  /// The colour register the luminances sit in. Its hue is what they are shades of; the mode's
  /// sixteen values replace the register's own luminance rather than adding to it.
  /// </param>
  /// <param name="shift">
  /// How far the picture is displaced horizontally. Formats that interlace two Graphics 9 fields
  /// offset them against each other by a pixel, which is what lets the pair resolve detail finer
  /// than either field's four-pixel-wide nibbles.
  /// </param>
  public static byte[] DecodeGr9Frame(
    ReadOnlySpan<byte> data, int offset, int stride, int width, int height, int background, int shift) {
    var gtia = Palette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var rowOffset = offset + y * stride;
      for (var x = 0; x < width; ++x) {
        var source = x + shift;
        var luminance = 0;
        if (source >= 0 && source < width) {
          var index = rowOffset + (source >> 3);
          // A nibble covers four screen pixels, high half of the byte first.
          if (index < data.Length)
            luminance = (data[index] >> (~source & 4)) & 15;
        }

        var entry = ((background | luminance) & 0xFF) * 3;
        var target = (y * width + x) * 3;
        rgb[target] = gtia[entry];
        rgb[target + 1] = gtia[entry + 1];
        rgb[target + 2] = gtia[entry + 2];
      }
    }

    return rgb;
  }

  /// <summary>Colour registers an ANTIC mode 4 line draws from: the background and PF0 to PF3.</summary>
  public const int Gr12RegisterCount = 5;

  /// <summary>
  /// Renders one character row of an ANTIC mode 4 ("Graphics 12") screen into a frame of GTIA
  /// colour bytes.
  /// </summary>
  /// <param name="characters">
  /// One character code per cell, or empty to take the codes from the horizontal position — which
  /// is how a font is displayed as a picture of itself.
  /// </param>
  /// <param name="registers">Background, PF0, PF1, PF2 and PF3, in that order.</param>
  /// <param name="width">Screen pixels across. Each of the four per byte is drawn two wide.</param>
  /// <param name="doubleLine">Whether each of the eight font rows covers two scanlines, as mode 5 does.</param>
  /// <remarks>
  /// Mode 4 is a text mode that spends two bits per pixel instead of one, so a character is four
  /// pixels wide rather than eight and can show four colours. The fourth is bought rather than
  /// given: setting a character code's high bit draws its pattern 3 from PF3 instead of PF2, which
  /// costs half the character set to gain one colour per cell.
  /// </remarks>
  public static void DecodeGr12Line(
    ReadOnlySpan<byte> characters, int charactersOffset, ReadOnlySpan<byte> font, int fontOffset,
    ReadOnlySpan<byte> registers, Span<byte> frame, int frameOffset, int width, bool doubleLine) {
    var rows = doubleLine ? 16 : 8;

    for (var y = 0; y < rows; ++y) {
      for (var x = 0; x < width; ++x) {
        var character = x >> 3;
        if (!characters.IsEmpty) {
          var at = charactersOffset + character;
          character = at >= 0 && at < characters.Length ? characters[at] : 0;
        }

        var index = fontOffset + ((character & 127) << 3) + (doubleLine ? y >> 1 : y);
        var pattern = index >= 0 && index < font.Length ? (font[index] >> (~x & 6)) & 3 : 0;
        var register = pattern == 3 && character >= 128 ? 4 : pattern;

        var target = frameOffset + x;
        if (target >= 0 && target < frame.Length)
          frame[target] = (byte)(register < registers.Length ? registers[register] & 254 : 0);
      }

      frameOffset += width;
    }
  }

  /// <summary>
  /// Reads five colour registers stored as PF0, PF1, PF2, PF3 and then the background, and returns
  /// them in the background-first order <see cref="DecodeGr12Line"/> takes.
  /// </summary>
  /// <remarks>
  /// The two orders both occur in the wild — a file stores the registers in the order the hardware
  /// wants them poked, while a pixel value indexes them with the background first — so which of the
  /// two a given number means is worth naming rather than leaving to the reader.
  /// </remarks>
  public static byte[] ReadPf0123Bak(ReadOnlySpan<byte> data, int offset) {
    var registers = new byte[Gr12RegisterCount];
    for (var i = 0; i < Gr12RegisterCount; ++i) {
      var at = offset + i;
      registers[i == Gr12RegisterCount - 1 ? 0 : i + 1] = at >= 0 && at < data.Length ? data[at] : (byte)0;
    }

    return registers;
  }

  /// <summary>Turns a frame of GTIA colour bytes into RGB triplets.</summary>
  public static byte[] ApplyPalette(ReadOnlySpan<byte> frame) {
    var gtia = Palette;
    var rgb = new byte[frame.Length * 3];

    for (var i = 0; i < frame.Length; ++i) {
      var entry = frame[i] * 3;
      rgb[i * 3] = gtia[entry];
      rgb[i * 3 + 1] = gtia[entry + 1];
      rgb[i * 3 + 2] = gtia[entry + 2];
    }

    return rgb;
  }

  /// <summary>
  /// Averages two frames channel by channel, which is what a display alternating between them
  /// looks like.
  /// </summary>
  /// <remarks>
  /// Several Atari programs show two pictures on alternate television fields to get colours the
  /// hardware cannot hold in its registers at one time. The eye averages them; so does this. The
  /// average rounds down, matching what the reference decoder produces, so the two agree exactly
  /// rather than approximately.
  /// </remarks>
  public static byte[] BlendFrames(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    => FrameBlend.Average(first, second);

  /// <summary>Logical pixels across an ANTIC mode 8 ("Graphics 3") line.</summary>
  public const int Gr3Width = 40;

  /// <summary>Logical rows in a Graphics 3 screen.</summary>
  public const int Gr3Height = 24;

  /// <summary>Bytes per Graphics 3 row: 40 pixels at 2 bits each.</summary>
  public const int Gr3BytesPerRow = Gr3Width / 4;

  /// <summary>Size of a Graphics 3 screen.</summary>
  public const int Gr3DataSize = Gr3BytesPerRow * Gr3Height;

  /// <summary>Unpacks an ANTIC mode 8 screen into one byte per logical pixel (values 0..3).</summary>
  /// <remarks>Mode 8 is the coarsest bitmap the hardware offers: 40x24 pixels, each drawn as an
  /// 8x8 block, which is why a whole screen fits in 240 bytes.</remarks>
  public static byte[] UnpackGr3(ReadOnlySpan<byte> data, int offset) {
    var pixels = new byte[Gr3Width * Gr3Height];
    for (var y = 0; y < Gr3Height; ++y)
    for (var x = 0; x < Gr3Width; ++x) {
      var index = offset + y * Gr3BytesPerRow + (x >> 2);
      if (index >= data.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      pixels[y * Gr3Width + x] = (byte)((data[index] >> shift) & 3);
    }

    return pixels;
  }

  /// <summary>Packs one byte per logical pixel (values 0..3) into the Graphics 3 layout.</summary>
  public static byte[] PackGr3(ReadOnlySpan<byte> pixels) {
    var data = new byte[Gr3DataSize];
    for (var y = 0; y < Gr3Height; ++y)
    for (var x = 0; x < Gr3Width; ++x) {
      var source = y * Gr3Width + x;
      if (source >= pixels.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      data[y * Gr3BytesPerRow + (x >> 2)] |= (byte)((pixels[source] & 3) << shift);
    }

    return data;
  }

  /// <summary>
  /// Unpacks an ANTIC mode F ("Graphics 9") row set into one luminance value (0..15) per logical
  /// pixel. Mode 9 stores two nibbles per byte, and each nibble covers four screen pixels, so a
  /// row of <paramref name="width"/> screen pixels occupies <c>width / 8</c> bytes.
  /// </summary>
  public static byte[] UnpackGr9(ReadOnlySpan<byte> data, int offset, int width, int rows) {
    var bytesPerRow = width >> 3;
    var pixels = new byte[width * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < width; ++x) {
      var index = offset + y * bytesPerRow + (x >> 3);
      if (index >= data.Length)
        break;

      // Nibbles run high first; each covers four consecutive pixels.
      var shift = (~x & 4);
      pixels[y * width + x] = (byte)((data[index] >> shift) & 15);
    }

    return pixels;
  }

  /// <summary>Packs luminance values (0..15) back into the Graphics 9 layout.</summary>
  public static byte[] PackGr9(ReadOnlySpan<byte> pixels, int width, int rows) {
    var bytesPerRow = width >> 3;
    var data = new byte[bytesPerRow * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < width; x += 4) {
      var source = y * width + x;
      if (source >= pixels.Length)
        break;

      var shift = (~x & 4);
      data[y * bytesPerRow + (x >> 3)] |= (byte)((pixels[source] & 15) << shift);
    }

    return data;
  }

  /// <summary>
  /// The 256 colours the GTIA produces, as measured from hardware rather than computed.
  /// </summary>
  /// <remarks>
  /// A colour byte is a hue in the high nibble and a luminance in the low one, but the chip does
  /// not lay them out on any tidy colour wheel: hue spacing is uneven, saturation falls off at the
  /// extremes of luminance, and the result differs between PAL and NTSC machines. A formula
  /// therefore gets the greys right and everything else visibly wrong, which is why this is a
  /// table. These are Altirra's PAL measurements, the same ones RECOIL decodes with, so our output
  /// and the reference agree exactly instead of approximately.
  /// </remarks>
  public static ReadOnlySpan<byte> Palette => [
    0x00, 0x00, 0x00, 0x11, 0x11, 0x11, 0x22, 0x22, 0x22, 0x33, 0x33, 0x33, 0x44, 0x44, 0x44, 0x55, 0x55, 0x55, 0x66, 0x66, 0x66, 0x77, 0x77, 0x77,
    0x88, 0x88, 0x88, 0x99, 0x99, 0x99, 0xAA, 0xAA, 0xAA, 0xBB, 0xBB, 0xBB, 0xCC, 0xCC, 0xCC, 0xDD, 0xDD, 0xDD, 0xEE, 0xEE, 0xEE, 0xFF, 0xFF, 0xFF,
    0x3F, 0x00, 0x00, 0x50, 0x05, 0x00, 0x61, 0x16, 0x00, 0x72, 0x27, 0x00, 0x83, 0x38, 0x00, 0x94, 0x49, 0x00, 0xA5, 0x5A, 0x01, 0xB6, 0x6B, 0x12,
    0xC7, 0x7C, 0x23, 0xD8, 0x8D, 0x34, 0xE9, 0x9E, 0x45, 0xFA, 0xAF, 0x56, 0xFF, 0xC0, 0x67, 0xFF, 0xD1, 0x78, 0xFF, 0xE2, 0x89, 0xFF, 0xF3, 0x9A,
    0x50, 0x00, 0x00, 0x61, 0x00, 0x00, 0x72, 0x03, 0x00, 0x83, 0x14, 0x03, 0x94, 0x25, 0x14, 0xA5, 0x36, 0x25, 0xB6, 0x47, 0x36, 0xC7, 0x58, 0x47,
    0xD8, 0x69, 0x58, 0xE9, 0x7A, 0x69, 0xFA, 0x8B, 0x7A, 0xFF, 0x9C, 0x8B, 0xFF, 0xAD, 0x9C, 0xFF, 0xBE, 0xAD, 0xFF, 0xCF, 0xBE, 0xFF, 0xE0, 0xCF,
    0x54, 0x00, 0x03, 0x65, 0x00, 0x14, 0x76, 0x00, 0x25, 0x87, 0x08, 0x36, 0x98, 0x19, 0x47, 0xA9, 0x2A, 0x58, 0xBA, 0x3B, 0x69, 0xCB, 0x4C, 0x7A,
    0xDC, 0x5D, 0x8B, 0xED, 0x6E, 0x9C, 0xFE, 0x7F, 0xAD, 0xFF, 0x90, 0xBE, 0xFF, 0xA1, 0xCF, 0xFF, 0xB2, 0xE0, 0xFF, 0xC3, 0xF1, 0xFF, 0xD4, 0xFF,
    0x4F, 0x00, 0x35, 0x60, 0x00, 0x46, 0x71, 0x00, 0x57, 0x82, 0x01, 0x68, 0x93, 0x12, 0x79, 0xA4, 0x23, 0x8A, 0xB5, 0x34, 0x9B, 0xC6, 0x45, 0xAC,
    0xD7, 0x56, 0xBD, 0xE8, 0x67, 0xCE, 0xF9, 0x78, 0xDF, 0xFF, 0x89, 0xF0, 0xFF, 0x9A, 0xFF, 0xFF, 0xAB, 0xFF, 0xFF, 0xBC, 0xFF, 0xFF, 0xCD, 0xFF,
    0x3D, 0x00, 0x68, 0x4E, 0x00, 0x79, 0x5F, 0x00, 0x8A, 0x70, 0x00, 0x9B, 0x81, 0x11, 0xAC, 0x92, 0x22, 0xBD, 0xA3, 0x33, 0xCE, 0xB4, 0x44, 0xDF,
    0xC5, 0x55, 0xF0, 0xD6, 0x66, 0xFF, 0xE7, 0x77, 0xFF, 0xF8, 0x88, 0xFF, 0xFF, 0x99, 0xFF, 0xFF, 0xAA, 0xFF, 0xFF, 0xBB, 0xFF, 0xFF, 0xCC, 0xFF,
    0x20, 0x00, 0x8B, 0x31, 0x00, 0x9C, 0x42, 0x00, 0xAD, 0x53, 0x08, 0xBE, 0x64, 0x19, 0xCF, 0x75, 0x2A, 0xE0, 0x86, 0x3B, 0xF1, 0x97, 0x4C, 0xFF,
    0xA8, 0x5D, 0xFF, 0xB9, 0x6E, 0xFF, 0xCA, 0x7F, 0xFF, 0xDB, 0x90, 0xFF, 0xEC, 0xA1, 0xFF, 0xFD, 0xB2, 0xFF, 0xFF, 0xC3, 0xFF, 0xFF, 0xD4, 0xFF,
    0x00, 0x00, 0x89, 0x00, 0x08, 0x9A, 0x00, 0x19, 0xAB, 0x10, 0x2A, 0xBC, 0x21, 0x3B, 0xCD, 0x32, 0x4C, 0xDE, 0x43, 0x5D, 0xEF, 0x54, 0x6E, 0xFF,
    0x65, 0x7F, 0xFF, 0x76, 0x90, 0xFF, 0x87, 0xA1, 0xFF, 0x98, 0xB2, 0xFF, 0xA9, 0xC3, 0xFF, 0xBA, 0xD4, 0xFF, 0xCB, 0xE5, 0xFF, 0xDC, 0xF6, 0xFF,
    0x00, 0x0C, 0x65, 0x00, 0x1D, 0x76, 0x00, 0x2E, 0x87, 0x00, 0x3F, 0x98, 0x05, 0x50, 0xA9, 0x16, 0x61, 0xBA, 0x27, 0x72, 0xCB, 0x38, 0x83, 0xDC,
    0x49, 0x94, 0xED, 0x5A, 0xA5, 0xFE, 0x6B, 0xB6, 0xFF, 0x7C, 0xC7, 0xFF, 0x8D, 0xD8, 0xFF, 0x9E, 0xE9, 0xFF, 0xAF, 0xFA, 0xFF, 0xC0, 0xFF, 0xFF,
    0x00, 0x1F, 0x30, 0x00, 0x30, 0x41, 0x00, 0x41, 0x52, 0x00, 0x52, 0x63, 0x00, 0x63, 0x74, 0x05, 0x74, 0x85, 0x16, 0x85, 0x96, 0x27, 0x96, 0xA7,
    0x38, 0xA7, 0xB8, 0x49, 0xB8, 0xC9, 0x5A, 0xC9, 0xDA, 0x6B, 0xDA, 0xEB, 0x7C, 0xEB, 0xFC, 0x8D, 0xFC, 0xFF, 0x9E, 0xFF, 0xFF, 0xAF, 0xFF, 0xFF,
    0x00, 0x2B, 0x00, 0x00, 0x3C, 0x0E, 0x00, 0x4D, 0x1F, 0x00, 0x5E, 0x30, 0x00, 0x6F, 0x41, 0x01, 0x80, 0x52, 0x12, 0x91, 0x63, 0x23, 0xA2, 0x74,
    0x34, 0xB3, 0x85, 0x45, 0xC4, 0x96, 0x56, 0xD5, 0xA7, 0x67, 0xE6, 0xB8, 0x78, 0xF7, 0xC9, 0x89, 0xFF, 0xDA, 0x9A, 0xFF, 0xEB, 0xAB, 0xFF, 0xFC,
    0x00, 0x33, 0x00, 0x00, 0x44, 0x00, 0x00, 0x55, 0x00, 0x00, 0x66, 0x00, 0x07, 0x77, 0x00, 0x18, 0x88, 0x00, 0x29, 0x99, 0x00, 0x3A, 0xAA, 0x0F,
    0x4B, 0xBB, 0x20, 0x5C, 0xCC, 0x31, 0x6D, 0xDD, 0x42, 0x7E, 0xEE, 0x53, 0x8F, 0xFF, 0x64, 0xA0, 0xFF, 0x75, 0xB1, 0xFF, 0x86, 0xC2, 0xFF, 0x97,
    0x00, 0x2B, 0x00, 0x00, 0x3C, 0x00, 0x02, 0x4D, 0x00, 0x13, 0x5E, 0x00, 0x24, 0x6F, 0x00, 0x35, 0x80, 0x00, 0x46, 0x91, 0x00, 0x57, 0xA2, 0x00,
    0x68, 0xB3, 0x00, 0x79, 0xC4, 0x0E, 0x8A, 0xD5, 0x1F, 0x9B, 0xE6, 0x30, 0xAC, 0xF7, 0x41, 0xBD, 0xFF, 0x52, 0xCE, 0xFF, 0x63, 0xDF, 0xFF, 0x74,
    0x01, 0x1C, 0x00, 0x12, 0x2D, 0x00, 0x23, 0x3E, 0x00, 0x34, 0x4F, 0x00, 0x45, 0x60, 0x00, 0x56, 0x71, 0x00, 0x67, 0x82, 0x00, 0x78, 0x93, 0x00,
    0x89, 0xA4, 0x00, 0x9A, 0xB5, 0x03, 0xAB, 0xC6, 0x14, 0xBC, 0xD7, 0x25, 0xCD, 0xE8, 0x36, 0xDE, 0xF9, 0x47, 0xEF, 0xFF, 0x58, 0xFF, 0xFF, 0x69,
    0x23, 0x09, 0x00, 0x34, 0x1A, 0x00, 0x45, 0x2B, 0x00, 0x56, 0x3C, 0x00, 0x67, 0x4D, 0x00, 0x78, 0x5E, 0x00, 0x89, 0x6F, 0x00, 0x9A, 0x80, 0x00,
    0xAB, 0x91, 0x00, 0xBC, 0xA2, 0x10, 0xCD, 0xB3, 0x21, 0xDE, 0xC4, 0x32, 0xEF, 0xD5, 0x43, 0xFF, 0xE6, 0x54, 0xFF, 0xF7, 0x65, 0xFF, 0xFF, 0x76,
    0x3F, 0x00, 0x00, 0x50, 0x05, 0x00, 0x61, 0x16, 0x00, 0x72, 0x27, 0x00, 0x83, 0x38, 0x00, 0x94, 0x49, 0x00, 0xA5, 0x5A, 0x01, 0xB6, 0x6B, 0x12,
    0xC7, 0x7C, 0x23, 0xD8, 0x8D, 0x34, 0xE9, 0x9E, 0x45, 0xFA, 0xAF, 0x56, 0xFF, 0xC0, 0x67, 0xFF, 0xD1, 0x78, 0xFF, 0xE2, 0x89, 0xFF, 0xF3, 0x9A,
  ];

  /// <summary>The GTIA palette as RGB triplets.</summary>
  public static byte[] CreatePalette() => Palette.ToArray();

  /// <summary>Finds the colour byte whose palette entry is closest to the given RGB value.</summary>
  /// <remarks>The hardware ignores the low bit of a colour byte, so only even values are considered.</remarks>
  public static byte FindNearestColorByte(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    var best = (byte)0;
    var bestDistance = int.MaxValue;
    for (var candidate = 0; candidate < 256; candidate += 2) {
      var offset = candidate * 3;
      int dr = palette[offset] - red, dg = palette[offset + 1] - green, db = palette[offset + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = (byte)candidate;
      if (distance == 0)
        break;
    }

    return best;
  }

}
