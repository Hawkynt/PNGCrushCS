using System;
using FileFormat.Core;

namespace FileFormat.ApplePreferred;

/// <summary>In-memory representation of an Apple Preferred Format picture (.32k).</summary>
/// <remarks>
/// The IIGS's own picture format, and the only one here built out of named chunks: a length, a name
/// and a body, repeated. Only two chunks matter. MAIN holds the palettes, a directory saying how
/// many bytes each scanline packs into and which palette it uses, and then the packed bitmap; the
/// optional MULTIPAL replaces the directory's choice with a palette per line, which is how a
/// picture gets more than sixteen colours.
/// <para/>
/// Two screen modes exist and they are not a resolution setting so much as two different pictures.
/// The 320-wide one is four bits a pixel against the whole palette. The 640-wide one is two bits a
/// pixel, but each of the four pixels in a byte draws from a different quarter of the palette, so a
/// row still shows all sixteen colours — and its rows are drawn twice, because at 640 across the
/// machine ran only 200 lines.
/// </remarks>
public readonly record struct ApplePreferredFile
  : IImageFormatReader<ApplePreferredFile>, IImageToRawImage<ApplePreferredFile>,
    IImageFromRawImage<ApplePreferredFile>, IImageFormatWriter<ApplePreferredFile> {

  /// <summary>Where the palettes start.</summary>
  public const int PalettesOffset = 15;

  /// <summary>Colours one palette holds, which in the 320 mode is all a pixel can name.</summary>
  public const int ColorCount = AppleIIGSGraphics.ColorCount;

  /// <summary>The largest side the header's sixteen-bit fields can state.</summary>
  public const int MaxSide = 65535;

  /// <summary>The shortest a file may be and still be recognised.</summary>
  public const int MinimumFileSize = 1249;

  /// <summary>Bytes a scanline's directory entry occupies.</summary>
  public const int DirectoryEntrySize = 4;

  /// <summary>Length of a MULTIPAL chunk: its header and two hundred palettes.</summary>
  public const int MultipalChunkSize = 6415;

  static string IImageFormatMetadata<ApplePreferredFile>.PrimaryExtension => ".32k";
  /// <summary>
  /// Also .shr, which is what both samples in the corpus carry.
  /// </summary>
  /// <remarks>
  /// Neither was read though this reader decodes both exactly as RECOIL does — one 320 by 514 and
  /// the other 560 by 384, neither of which is a size a guess would have landed on.
  /// </remarks>
  static string[] IImageFormatMetadata<ApplePreferredFile>.FileExtensions => [".32k", ".gs", ".iigs", ".shr"];
  static ApplePreferredFile IImageFormatReader<ApplePreferredFile>.FromSpan(ReadOnlySpan<byte> data)
    => ApplePreferredReader.FromSpan(data);
  static byte[] IImageFormatWriter<ApplePreferredFile>.ToBytes(ApplePreferredFile file)
    => ApplePreferredWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ApplePreferredFile>.VideoModes => [
    new("Apple IIGS", [(new(1, 640), new(1, 400))], [3200])
  ];

  /// <summary>The whole file, which every offset is relative to.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows the picture is drawn as, which is twice the stored count in the 640 mode.</summary>
  public int Height { get; init; }

  /// <summary>Stored scanlines.</summary>
  public int StoredHeight { get; init; }

  /// <summary>Whether the picture is the 640-wide two-bit mode.</summary>
  public bool IsWideMode { get; init; }

  /// <summary>Where the scanline directory starts.</summary>
  public int DirectoryOffset { get; init; }

  /// <summary>Where the packed bitmap starts.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Where the per-scanline palettes start, or -1 if the picture has none.</summary>
  public int MultipalOffset { get; init; }

  public static RawImage ToRawImage(ApplePreferredFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[file.Width * file.Height * 3];
    var bytesPerLine = file.IsWideMode ? file.Width >> 2 : file.Width >> 1;
    var stream = new PackBytesStream(file.BitmapOffset);

    for (var y = 0; y < file.StoredHeight; ++y) {
      var entry = file.DirectoryOffset + y * DirectoryEntrySize;
      var palette = file.MultipalOffset >= 0
        ? AppleIIGSGraphics.ReadPalette(data, file.MultipalOffset + y * AppleIIGSGraphics.PaletteSize, reversed: false)
        : AppleIIGSGraphics.ReadPalette(
          data, PalettesOffset + (data[entry + 2] & 15) * AppleIIGSGraphics.PaletteSize, reversed: false);

      // Each line says how many packed bytes it occupies, so a line that unpacks short does not
      // drag the rest of the picture out of step.
      var nextLine = stream.Offset + data[entry] + (data[entry + 1] << 8);

      for (var x = 0; x < bytesPerLine; ++x) {
        var value = stream.ReadByte(data);
        if (value < 0)
          throw new System.IO.InvalidDataException($"Scanline {y} ends before the picture does.");

        if (file.IsWideMode)
          _WriteWide(rgb, palette, file.Width, y, x, value);
        else
          _WriteNarrow(rgb, palette, file.Width, y, x, value);
      }

      stream.Offset = nextLine;
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Draws the two pixels of a 320-mode byte.</summary>
  private static void _WriteNarrow(Span<byte> rgb, ReadOnlySpan<byte> palette, int width, int y, int x, int value) {
    _Plot(rgb, palette, (y * width + (x << 1)) * 3, value >> 4);
    _Plot(rgb, palette, (y * width + (x << 1) + 1) * 3, value & 15);
  }

  /// <summary>
  /// Draws the four pixels of a 640-mode byte, on the two rows it occupies.
  /// </summary>
  /// <remarks>
  /// The quarters are taken in the order 8, 12, 0, 4 rather than 0, 4, 8, 12. That is not a
  /// convention but where the bits land: the hardware reads the byte's pairs in the order it does
  /// and pairs them with palette quarters in the order it does, and the two orders do not agree.
  /// </remarks>
  private static void _WriteWide(Span<byte> rgb, ReadOnlySpan<byte> palette, int width, int y, int x, int value) {
    ReadOnlySpan<int> quarters = [8, 12, 0, 4];

    for (var i = 0; i < 4; ++i) {
      var index = quarters[i] + ((value >> (6 - i * 2)) & 3);
      var at = ((y << 1) * width + (x << 2) + i) * 3;
      _Plot(rgb, palette, at, index);
      _Plot(rgb, palette, at + width * 3, index);
    }
  }

  private static void _Plot(Span<byte> rgb, ReadOnlySpan<byte> palette, int target, int index) {
    var entry = index * 3;
    rgb[target] = palette[entry];
    rgb[target + 1] = palette[entry + 1];
    rgb[target + 2] = palette[entry + 2];
  }

  /// <summary>Encodes a picture in the 320 mode: four bits a pixel against one palette of sixteen.</summary>
  /// <remarks>
  /// The 640 mode is not written. It shows sixteen colours across a row only because each of a
  /// byte's four pixels draws from a different quarter of the palette, so a pixel there chooses
  /// between four colours and not sixteen — which is a worse picture from the same bytes unless the
  /// picture was drawn for it.
  /// <para/>
  /// Per-line palettes are not written either. They live in a second chunk the reader only looks for
  /// in a picture exactly two hundred lines tall, so a file carrying them would be a picture at one
  /// height and a different one at every other.
  /// <para/>
  /// A picture states its own size, so nothing is scaled to a screen; only an odd width is moved,
  /// since a byte holds two pixels and half a byte cannot be stored.
  /// </remarks>
  public static ApplePreferredFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width > MaxSide || image.Height > MaxSide)
      throw new ArgumentException(
        $"An Apple Preferred header states its size in sixteen bits, so {image.Width}x{image.Height} cannot be written.",
        nameof(image));

    var width = Math.Max(2, image.Width & ~1);
    var height = Math.Max(1, image.Height);
    var source = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);
    var indexed = source.EnsureIndexedAtMost(ColorCount);

    var bytesPerLine = width >> 1;
    var lines = new byte[height][];
    for (var y = 0; y < height; ++y) {
      var line = new byte[bytesPerLine];
      for (var x = 0; x < bytesPerLine; ++x) {
        var at = y * width + (x << 1);
        line[x] = (byte)((indexed.PixelData[at] << 4) | (indexed.PixelData[at + 1] & 15));
      }

      lines[y] = _PackBytes(line);
    }

    var directoryOffset = PalettesOffset + 2 + AppleIIGSGraphics.PaletteSize;
    var bitmapOffset = directoryOffset + height * DirectoryEntrySize;
    var total = bitmapOffset;
    foreach (var line in lines)
      total += line.Length;

    // A file shorter than the reader's floor is padded rather than refused; the chunk's own length
    // covers the padding, so the chunk walk still ends where the file does.
    var data = new byte[Math.Max(total, MinimumFileSize)];
    data[0] = (byte)data.Length;
    data[1] = (byte)(data.Length >> 8);
    data[2] = (byte)(data.Length >> 16);
    data[3] = (byte)(data.Length >> 24);
    data[4] = 4;
    data[5] = (byte)'M';
    data[6] = (byte)'A';
    data[7] = (byte)'I';
    data[8] = (byte)'N';
    data[11] = (byte)width;
    data[12] = (byte)(width >> 8);
    data[13] = 1;

    var palette = indexed.Palette ?? [];
    for (var i = 0; i < ColorCount; ++i) {
      var entry = PalettesOffset + (i << 1);
      var source3 = i * 3;

      // Green and blue share the low byte and red is alone in the high one, four bits each.
      data[entry] = (byte)((_Nibble(palette, source3 + 1) << 4) | _Nibble(palette, source3 + 2));
      data[entry + 1] = _Nibble(palette, source3);
    }

    data[directoryOffset - 2] = (byte)height;
    data[directoryOffset - 1] = (byte)(height >> 8);

    var at2 = bitmapOffset;
    for (var y = 0; y < height; ++y) {
      var entry = directoryOffset + y * DirectoryEntrySize;
      data[entry] = (byte)lines[y].Length;
      data[entry + 1] = (byte)(lines[y].Length >> 8);
      lines[y].CopyTo(data, at2);
      at2 += lines[y].Length;
    }

    return new() {
      Data = data,
      Width = width,
      Height = height,
      StoredHeight = height,
      IsWideMode = false,
      DirectoryOffset = directoryOffset,
      BitmapOffset = bitmapOffset,
      MultipalOffset = -1,
    };
  }

  /// <summary>
  /// Packs a scanline as PackBytes literals, sixty-four bytes to a command.
  /// </summary>
  /// <remarks>
  /// Only the literal command is used. The other three step the read position backwards to repeat a
  /// byte or a four-byte pattern, and a run of literals costs one byte in sixty-four — which on a
  /// picture that is going to be looked at rather than shipped on a floppy is not worth a second
  /// pass over every scanline.
  /// </remarks>
  private static byte[] _PackBytes(ReadOnlySpan<byte> line) {
    var packed = new byte[line.Length + (line.Length + 63) / 64];
    var at = 0;

    for (var from = 0; from < line.Length; from += 64) {
      var take = Math.Min(64, line.Length - from);
      packed[at++] = (byte)(take - 1);
      line.Slice(from, take).CopyTo(packed.AsSpan(at));
      at += take;
    }

    return packed;
  }

  /// <summary>A channel back down to the four bits a IIGS palette stores it in.</summary>
  private static byte _Nibble(ReadOnlySpan<byte> palette, int index)
    => (byte)(index < palette.Length ? (palette[index] * 15 + 127) / 255 : 0);
}
