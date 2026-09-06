using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Spectrum512Smoosh;

/// <summary>In-memory representation of an Atari ST Spectrum 512 Smooshed (SPS) image (320x199, 512 colors).</summary>
/// <remarks>
/// Smooshed is a third-party repacking of a Spectrum 512 picture: the same 51,104 bytes an
/// <c>.spu</c> holds — 199 scanlines of four interleaved bitplanes, then 48 palette entries a line —
/// packed twice over. The bitmap goes through a run-length coder of its own, and the palette through
/// a bit-level one that names only the entries a line actually uses.
/// <para/>
/// The layout is Shamus McBride's, as written up in the <em>Atari ST Picture Formats</em> reference
/// and in the MultimediaWiki's Spectrum 512 entry; the two agree on the header, on both run-length
/// conventions, on the fourteen-bit palette map and on entries 0 and 15 being black by definition.
/// What neither settles is how a reader tells the two bitmap orders apart, because nothing in the
/// file says — see <c>_Unpack</c>.
/// </remarks>
public readonly record struct Spectrum512SmooshFile : IImageFormatReader<Spectrum512SmooshFile>, IImageToRawImage<Spectrum512SmooshFile>, IImageFromRawImage<Spectrum512SmooshFile>, IImageFormatWriter<Spectrum512SmooshFile> {

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 4;

  /// <summary>The unpacked picture, laid out exactly as an uncompressed <c>.spu</c> holds it.</summary>
  public const int DecompressedSize = 51104;

  /// <summary>Signature, unpacked bitmap length, unpacked palette length.</summary>
  public const int HeaderSize = 12;

  /// <summary>Colours one scanline can name; sixteen at a time, in three overlapping zones.</summary>
  public const int PaletteEntriesPerLine = 48;

  /// <summary>
  /// Colours a scanline can carry beside black — fourteen, not sixteen.
  /// </summary>
  /// <remarks>
  /// The packed palette states its entries behind a fourteen-bit map whose most significant bit
  /// stands for entry 1 and whose least significant stands for entry 14. Entries 0 and 15 have no bit
  /// and are black by definition, in every one of the three zones a line holds, so pen 0 and pen 15
  /// draw black wherever they appear and only pens 1 to 14 can name a colour.
  /// </remarks>
  public const int MaxColorsPerScanline = 14;

  private const int _SCANLINE_COUNT = 199;
  private const int _WIDTH = 320;
  private const int _BYTES_PER_SCANLINE = 160;
  private const int _BITMAP_OFFSET = 160;
  private const int _PALETTE_OFFSET = 32000;
  private const int _COLUMNS_PER_SCANLINE = 40;

  static string IImageFormatMetadata<Spectrum512SmooshFile>.PrimaryExtension => ".sps";
  static string[] IImageFormatMetadata<Spectrum512SmooshFile>.FileExtensions => [".sps"];
  static VideoMode[] IImageFormatMetadata<Spectrum512SmooshFile>.VideoModes => [new("Default", [(_WIDTH, _SCANLINE_COUNT)], [MaxColorsPerScanline])];
  static Spectrum512SmooshFile IImageFormatReader<Spectrum512SmooshFile>.FromSpan(ReadOnlySpan<byte> data) => Spectrum512SmooshReader.FromSpan(data);
  static byte[] IImageFormatWriter<Spectrum512SmooshFile>.ToBytes(Spectrum512SmooshFile file) => Spectrum512SmooshWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => _WIDTH;

  /// <summary>Always 199.</summary>
  public int Height => _SCANLINE_COUNT;

  /// <summary>The raw smooshed data bytes.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Unpacks the picture and paints it through the palette each scanline carries.</summary>
  /// <remarks>
  /// Which colour a pixel reads depends on how far along the scanline it sits: the ST reloads its
  /// palette registers twice while the beam crosses, so a line stores three sets of sixteen and each
  /// register switches at its own position. <see cref="FileFormat.Spectrum512.Spectrum512File.PaletteEntryFor"/>
  /// is that rule, and reading the sets as fixed thirds instead puts a sixth of the picture in the
  /// wrong colours.
  /// </remarks>
  public static RawImage ToRawImage(Spectrum512SmooshFile file) {
    var unpacked = _Unpack(file.RawData);
    var chunky = PlanarConverter.AtariStToChunky(unpacked.AsSpan(_BITMAP_OFFSET, _SCANLINE_COUNT * _BYTES_PER_SCANLINE), _WIDTH, _SCANLINE_COUNT, 4);
    var rgb = new byte[_WIDTH * _SCANLINE_COUNT * 3];

    for (var y = 0; y < _SCANLINE_COUNT; ++y) {
      var paletteOffset = _PALETTE_OFFSET + y * PaletteEntriesPerLine * 2;
      for (var x = 0; x < _WIDTH; ++x) {
        var entry = FileFormat.Spectrum512.Spectrum512File.PaletteEntryFor(chunky[y * _WIDTH + x], x);
        var colour = BinaryPrimitives.ReadUInt16BigEndian(unpacked.AsSpan(paletteOffset + entry * 2));
        var offset = (y * _WIDTH + x) * 3;
        rgb[offset] = ChannelScaling.Expand3((colour >> 8) & 7);
        rgb[offset + 1] = ChannelScaling.Expand3((colour >> 4) & 7);
        rgb[offset + 2] = ChannelScaling.Expand3(colour & 7);
      }
    }

    return new() {
      Width = _WIDTH,
      Height = _SCANLINE_COUNT,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Packs any picture, sampling it to 320x199 and reducing what a scanline cannot hold.</summary>
  /// <remarks>
  /// This is the registry's writer contract, which asks for a file out of an arbitrary picture, so a
  /// scanline past <see cref="MaxColorsPerScanline"/> colours is reduced to that many rather than
  /// turned down. A scanline already inside the budget is left exactly as it is — the reduction runs
  /// per line and only on the lines that need it — so a picture the format can hold comes back
  /// unchanged through this path as well.
  /// <para/>
  /// <see cref="FromExactRawImage"/> is the same encoder with the reduction taken away: it names what
  /// it cannot hold instead of approximating it, for callers who need the guarantee rather than a
  /// file.
  /// <para/>
  /// Colours are snapped to the ST's own palette either way — three bits a primary is every colour
  /// the machine has, and a Spectrum 512 file has nowhere to put anything else.
  /// </remarks>
  public static Spectrum512SmooshFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return _Encode(image.SampleTo(_WIDTH, _SCANLINE_COUNT).EnsureFormat(PixelFormat.Bgra32), reduce: true);
  }

  /// <summary>Packs a picture the format holds exactly, and refuses one it does not.</summary>
  /// <remarks>
  /// Exactly 320x199, and at most <see cref="MaxColorsPerScanline"/> colours beside black on every
  /// scanline. Anything else is refused by name — with the scanline and the count — rather than
  /// quantised down to fit, because a picture that came back with a sixth of its colours replaced by
  /// the nearest survivor would not be the picture that went in and nothing in the file would say so.
  /// </remarks>
  public static Spectrum512SmooshFile FromExactRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != _WIDTH || image.Height != _SCANLINE_COUNT)
      throw new NotSupportedException($"A Spectrum 512 Smooshed picture is exactly {_WIDTH}x{_SCANLINE_COUNT}; {image.Width}x{image.Height} is not one.");

    return _Encode(image.EnsureFormat(PixelFormat.Bgra32), reduce: false);
  }

  /// <summary>
  /// Assigns pens a scanline at a time and packs the result.
  /// </summary>
  /// <remarks>
  /// All three zones of a line are written with the same sixteen entries. That costs nothing, and it
  /// means the picture is what it is whatever a decoder believes about where the registers reload.
  /// </remarks>
  private static Spectrum512SmooshFile _Encode(RawImage source, bool reduce) {
    var chunky = new byte[_WIDTH * _SCANLINE_COUNT];
    var palettes = new ushort[_SCANLINE_COUNT][];
    var pens = new Dictionary<ushort, byte>(MaxColorsPerScanline);
    byte[]? scanline = null;

    for (var y = 0; y < _SCANLINE_COUNT; ++y) {
      pens.Clear();
      var palette = new ushort[16];
      var overflowed = false;

      for (var x = 0; x < _WIDTH; ++x) {
        var offset = (y * _WIDTH + x) * 4;
        var colour = _ToStColour(source.PixelData[offset + 2], source.PixelData[offset + 1], source.PixelData[offset]);

        // Black rides on pen 0, which the format keeps black anyway, so it never costs one of the
        // fourteen a line has to spend.
        byte pen;
        if (colour == 0)
          pen = 0;
        else if (!pens.TryGetValue(colour, out pen)) {
          if (pens.Count >= MaxColorsPerScanline) {
            overflowed = true;
            break;
          }

          pen = (byte)(pens.Count + 1);
          pens[colour] = pen;
          palette[pen] = colour;
        }

        chunky[y * _WIDTH + x] = pen;
      }

      if (overflowed) {
        if (!reduce)
          throw new NotSupportedException(
            $"Scanline {y} needs more than {MaxColorsPerScanline} colours beside black, which is all a Spectrum 512 Smooshed scanline can name. "
            + "Reduce the picture to that many colours a line before writing it.");

        Array.Clear(palette);
        scanline ??= new byte[_WIDTH * 4];
        source.PixelData.AsSpan(y * _WIDTH * 4, _WIDTH * 4).CopyTo(scanline);

        var reduced = ColorQuantizer.Quantize(scanline, _WIDTH, MaxColorsPerScanline);
        for (var entry = 0; entry < reduced.Count; ++entry)
          palette[entry + 1] = _ToStColour(reduced.Palette[entry * 3], reduced.Palette[entry * 3 + 1], reduced.Palette[entry * 3 + 2]);

        for (var x = 0; x < _WIDTH; ++x)
          chunky[y * _WIDTH + x] = (byte)(reduced.Indices[x] + 1);
      }

      palettes[y] = palette;
    }

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, _WIDTH, _SCANLINE_COUNT, 4);

    return new() { RawData = _Pack(planar, palettes) };
  }

  /// <summary>Rounds a full-colour pixel onto the ST's three-bits-a-primary palette.</summary>
  private static ushort _ToStColour(byte red, byte green, byte blue)
    => (ushort)(((red * 7 + 127) / 255 << 8) | ((green * 7 + 127) / 255 << 4) | ((blue * 7 + 127) / 255));

  #region packing

  private static byte[] _Pack(byte[] planar, ushort[][] palettes) {
    var bitmap = _PackBitmap(planar);
    var palette = _PackPalettes(palettes);

    var result = new byte[HeaderSize + bitmap.Length + palette.Length];
    result[0] = (byte)'S';
    result[1] = (byte)'P';
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)bitmap.Length);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)palette.Length);
    bitmap.CopyTo(result.AsSpan(HeaderSize));
    palette.CopyTo(result.AsSpan(HeaderSize + bitmap.Length));

    return result;
  }

  /// <summary>
  /// Run-length codes the bitmap in the byte-wide vertical strips the smooshed layout walks.
  /// </summary>
  /// <remarks>
  /// The two layouts a smooshed file may use are not distinguished by anything the file states; the
  /// reference decoder tells them apart by the parity of the very last byte, and <see cref="_PackPalettes"/>
  /// makes sure ours ends on an even one.
  /// </remarks>
  private static byte[] _PackBitmap(byte[] planar) {
    var ordered = new byte[_SCANLINE_COUNT * _BYTES_PER_SCANLINE];
    var at = 0;

    for (var plane = 0; plane < 8; plane += 2)
    for (var column = 0; column < _COLUMNS_PER_SCANLINE; ++column) {
      var byteInLine = ((column & ~1) << 2) + plane + (column & 1);
      for (var line = 0; line < _SCANLINE_COUNT; ++line)
        ordered[at++] = planar[line * _BYTES_PER_SCANLINE + byteInLine];
    }

    return _RunLengthEncode(ordered);
  }

  /// <summary>Run of three to 130 equal bytes, or one to 128 bytes taken as they come.</summary>
  private static byte[] _RunLengthEncode(byte[] data) {
    const int minimumRun = 3;
    const int maximumRun = 130;
    const int maximumLiteral = 128;

    var result = new List<byte>(data.Length / 2);
    var literalStart = 0;

    void FlushLiterals(int end) {
      while (literalStart < end) {
        var take = Math.Min(maximumLiteral, end - literalStart);
        result.Add((byte)(127 + take));
        for (var i = 0; i < take; ++i)
          result.Add(data[literalStart + i]);

        literalStart += take;
      }
    }

    for (var at = 0; at < data.Length;) {
      var run = 1;
      while (run < maximumRun && at + run < data.Length && data[at + run] == data[at])
        ++run;

      if (run < minimumRun) {
        at += run;
        continue;
      }

      FlushLiterals(at);
      result.Add((byte)(run - minimumRun));
      result.Add(data[at]);
      at += run;
      literalStart = at;
    }

    FlushLiterals(data.Length);

    return [.. result];
  }

  private static byte[] _PackPalettes(ushort[][] palettes) {
    var bits = new _BitWriter(_SCANLINE_COUNT * 3 * 20);

    foreach (var palette in palettes) {
      var present = 0;
      for (var entry = 1; entry <= MaxColorsPerScanline; ++entry)
        if (palette[entry] != 0)
          present |= 1 << (MaxColorsPerScanline - entry);

      // The same sixteen entries for each of the three zones the scanline holds.
      for (var zone = 0; zone < 3; ++zone) {
        bits.Write(present, 14);
        for (var entry = 1; entry <= MaxColorsPerScanline; ++entry) {
          var colour = palette[entry];
          if (colour != 0)
            bits.Write((((colour >> 8) & 7) << 6) | (((colour >> 4) & 7) << 3) | (colour & 7), 9);
        }
      }
    }

    return bits.ToArrayEndingEven();
  }

  private sealed class _BitWriter(int capacity) {

    private readonly List<byte> _bytes = new(capacity);
    private int _accumulator;
    private int _pending;

    public void Write(int value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        this._accumulator = (this._accumulator << 1) | ((value >> bit) & 1);
        if (++this._pending != 8)
          continue;

        this._bytes.Add((byte)this._accumulator);
        this._accumulator = 0;
        this._pending = 0;
      }
    }

    /// <summary>
    /// Flushes, then guarantees an even final byte — which is what says the bitmap was packed in
    /// vertical strips rather than plane by plane.
    /// </summary>
    public byte[] ToArrayEndingEven() {
      if (this._pending > 0) {
        this._bytes.Add((byte)(this._accumulator << (8 - this._pending)));
        this._accumulator = 0;
        this._pending = 0;
      }

      if (this._bytes.Count == 0 || (this._bytes[^1] & 1) != 0)
        this._bytes.Add(0);

      return [.. this._bytes];
    }
  }

  #endregion

  #region unpacking

  private static byte[] _Unpack(byte[] data) {
    if (data is null || data.Length < 13)
      throw new InvalidDataException($"Data too small for a Spectrum 512 Smooshed picture: got {data?.Length ?? 0} bytes.");

    if (data[0] != 'S' || data[1] != 'P' || data[2] != 0 || data[3] != 0)
      throw new InvalidDataException("Not a Spectrum 512 Smooshed picture: the \"SP\" signature and its two reserved zero bytes are missing.");

    var unpacked = new byte[DecompressedSize];
    var rle = new _RunLengthReader(data, HeaderSize);

    // Nothing in the file says which of the two layouts packed it. The reference decoder reads that
    // from the parity of the last byte, and so does this.
    if ((data[^1] & 1) == 0)
      for (var plane = 0; plane < 8; plane += 2)
      for (var column = 0; column < _COLUMNS_PER_SCANLINE; ++column)
        rle.Unpack(unpacked, _BITMAP_OFFSET + ((column & ~1) << 2) + plane + (column & 1), _BYTES_PER_SCANLINE);
    else
      for (var plane = 0; plane < 8; plane += 2)
        rle.UnpackWords(unpacked, _BITMAP_OFFSET + plane, 8);

    var paletteStart = HeaderSize + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
    if (paletteStart < HeaderSize || paletteStart >= data.Length)
      throw new InvalidDataException($"The palette offset states {paletteStart}, which is outside a {data.Length}-byte file.");

    var bits = new _BitReader(data, paletteStart);
    for (var offset = _PALETTE_OFFSET; offset < DecompressedSize;) {
      // Fourteen entries are stated, entry 1 first; entries 0 and 15 have no bit and stay black.
      var present = bits.Read(14) << 1;
      for (var bit = 15; bit >= 0; --bit) {
        var colour = (present >> bit & 1) == 0 ? 0 : bits.Read(9);
        unpacked[offset] = (byte)(colour >> 6);
        unpacked[offset + 1] = (byte)(((colour & 0x38) << 1) | (colour & 7));
        offset += 2;
      }
    }

    return unpacked;
  }

  private sealed class _RunLengthReader(byte[] data, int offset) {

    private int _offset = offset;
    private int _remaining;
    private int _value;

    private int _Next() {
      while (this._remaining == 0) {
        if (this._offset >= data.Length)
          throw new InvalidDataException("The packed bitmap ends before the picture is complete.");

        var command = data[this._offset++];
        if (command < 128) {
          if (this._offset >= data.Length)
            throw new InvalidDataException("The packed bitmap ends inside a run.");

          this._remaining = command + 3;
          this._value = data[this._offset++];
        } else {
          this._remaining = command - 127;
          this._value = -1;
        }
      }

      --this._remaining;
      if (this._value >= 0)
        return this._value;

      if (this._offset >= data.Length)
        throw new InvalidDataException("The packed bitmap ends inside a literal.");

      return data[this._offset++];
    }

    /// <summary>Fills one byte-wide column, top to bottom.</summary>
    public void Unpack(byte[] destination, int at, int stride) {
      for (; at < _PALETTE_OFFSET; at += stride)
        destination[at] = (byte)this._Next();
    }

    /// <summary>Fills one bitplane word by word, the way an <c>.spc</c> stores it.</summary>
    public void UnpackWords(byte[] destination, int at, int stride) {
      for (; at < _PALETTE_OFFSET; at += stride) {
        destination[at] = (byte)this._Next();
        destination[at + 1] = (byte)this._Next();
      }
    }
  }

  private sealed class _BitReader(byte[] data, int offset) {

    private int _offset = offset;
    private int _bits;
    private int _pending;

    public int Read(int count) {
      var result = 0;
      while (--count >= 0) {
        if (this._pending == 0) {
          if (this._offset >= data.Length)
            throw new InvalidDataException("The packed palette ends before every scanline has one.");

          this._bits = data[this._offset++];
          this._pending = 8;
        }

        --this._pending;
        result = (result << 1) | ((this._bits >> this._pending) & 1);
      }

      return result;
    }
  }

  #endregion

}
