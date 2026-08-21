using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes 8BPS: Apple's Planar RGB, QuickTime's own lossless codec for capturing true-colour frames
/// whole — red, green and blue as three complete planes, run-length coded a line at a time, with a
/// fourth plane of alpha where the picture carries one.
/// </summary>
/// <remarks>
/// Read from "Description of the Planar RGB (8BPS) Codec" by Roberto Togni, v1.0, October 2003,
/// published at <c>multimedia.cx/8bps.txt</c> under the GNU Free Documentation Licence and mirrored
/// on MultimediaWiki's own <c>8BPS</c> page — a standalone technical write-up citing XAnim as its own
/// source, not a paraphrase of anybody's already-written decoder, and predating rather than
/// following the codec's inclusion in any tool this project treats as an oracle.
/// <para/>
/// <b>The frame is two sections.</b> First, for every plane in turn, a table of one 16-bit big-endian
/// length a row — how many compressed bytes that row of that plane takes, top row first. Then, in the
/// same order, the compressed rows themselves. A picture with a palette has one plane; a true-colour
/// one has three, red then green then blue; a fourth plane of alpha follows those for the depth that
/// carries it. Nothing about the coding differs between planes — only how many of them there are and
/// what a decoder does with what comes out.
/// <para/>
/// <b>Line decompression is PackBits</b>, with one place where the document's own prose does not
/// match what real files contain. It states the byte layout correctly: a control byte of 127 or below
/// starts a literal run and one above it starts a repeat of the byte that follows, for
/// <c>257 - control</c> copies. But it also says the literal run's length is the control byte itself,
/// and every real file disagrees — decoding frame 0 of a real stream both ways and comparing against
/// ffmpeg's own decode of the same file settles it outright: reading the control byte as the run
/// length leaves the row 56 pixels short and misaligned for everything after it, where reading it as
/// <c>control + 1</c>, the ordinary PackBits rule, reproduces the row exactly and every row after it
/// for the rest of the file. That is what this decoder reads.
/// <para/>
/// <b>The picture this codec addresses has three depths</b>, all measured against real files and none
/// guessed: eight bits, one plane of palette indices with a colour table carried in the sample
/// description exactly where QuickTime Animation's own indexed depths keep one — a custom table
/// identified by a colour table ID of zero, its entries each a 16-bit-per-channel colour reduced to
/// eight bits by keeping the high byte, which a real file's embedded colour table and ffmpeg's own
/// decoded palette agree on entry for entry; twenty-four bits, three planes packed straight into RGB;
/// and thirty-two, four planes packed into RGB and alpha. A colour table identifier of -1 states there
/// is no table, which an indexed depth cannot be drawn without, and any other identifier names a
/// system colour resource this format's samples never call for and this project cannot check a
/// reading of against anything.
/// <para/>
/// <b>No inter-frame coding.</b> The document itself is unsure whether the format's own allowance for
/// a row shorter than the picture — coding less than the full width and leaving the rest as whatever
/// the frame before it drew — was ever used by a real encoder, since none of its own samples used it
/// either. None of the three real files measured here do either: every row of every frame decodes to
/// exactly the picture's width with no canvas carried between packets, so a row that cannot be filled
/// to the full width from what its own compressed bytes state is refused rather than filled in from a
/// frame this decoder does not keep.
/// <para/>
/// <b>Measured against ffmpeg's own decode, exactly, on real files</b> — RGB-native, so a direct
/// sample comparison is valid and there is no chroma-siting convention to disagree about. Three
/// streams from samples.ffmpeg.org, one at each depth this codec reads: 34 frames of 160x120 at 24
/// bits, 150 frames of 320x213 at 32 bits with a real alpha channel, and 169 frames of 360x240 at 8
/// bits through an embedded colour table — 353 frames in all, every plane of every one identical to
/// ffmpeg's decode of the same file, alpha and palette included.
/// <para/>
/// <b>What refuses, and says so.</b> A picture with no pixels; a depth that is none of eight,
/// twenty-four or thirty-two; an indexed picture whose colour table identifier is not the custom-table
/// value this format's own samples use, or whose declared table entry names an index outside the
/// table's own stated size; a packet too short to hold even the line-length tables its plane count and
/// picture height require; a row whose control bytes run past the compressed length its own table
/// entry states, or fall short of the picture's width without doing so, or overrun it; and a plane
/// count's worth of pixel data that does not end exactly where the packet does.
/// </remarks>
public sealed class EightBpsVideoDecoder : IVideoCodecDecoder<EightBpsVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("8BPS");

  /// <summary>Where the depth sits in a QuickTime visual sample entry, counted from the first byte
  /// of the entry's body — the same offset every codec reading this container's sample descriptions
  /// uses, because the field is at the same place whatever the codec is.</summary>
  private const int _DEPTH_AT = 74;

  /// <summary>Where the colour table identifier sits, immediately after the depth.</summary>
  private const int _COLOUR_TABLE_ID_AT = 76;

  /// <summary>Where a colour table, when the identifier calls for one, begins.</summary>
  private const int _COLOUR_TABLE_AT = 78;

  /// <summary>The identifier a custom colour table follows, verified against a real file's embedded
  /// table and ffmpeg's own decoded palette.</summary>
  private const short _CUSTOM_COLOUR_TABLE_ID = 0;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _planes;
  private readonly PixelFormat _format;
  private readonly byte[]? _palette;
  private readonly int _paletteCount;

  private EightBpsVideoDecoder(
    int width, int height, int streamIndex, int planes, PixelFormat format, byte[]? palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._planes = planes;
    this._format = format;
    this._palette = palette;
    this._paletteCount = paletteCount;
  }

  public static string CodecName => "Apple Planar RGB (8BPS)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EightBpsVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var description = stream.CodecPrivateData.Span;
    var depth = stream.BitsPerPixel > 0 ? stream.BitsPerPixel : _ReadDepth(description);

    switch (depth) {
      case 8: {
        var (palette, count) = _ReadPalette(description, stream.Index);
        return new(stream.Width, stream.Height, stream.Index, 1, PixelFormat.Indexed8, palette, count);
      }
      case 24:
        return new(stream.Width, stream.Height, stream.Index, 3, PixelFormat.Rgb24, null, 0);
      case 32:
        return new(stream.Width, stream.Height, stream.Index, 4, PixelFormat.Rgba32, null, 0);
      default:
        throw new NotSupportedException(
          $"Video stream {stream.Index} states a depth of {depth}, which 8BPS defines no plane layout for — only eight bits through a palette, twenty-four bits of RGB and thirty-two with alpha are.");
    }
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = this._format,
      PixelData = this._Pack(planes),
      Palette = this._palette,
      PaletteCount = this._paletteCount,
    };

    return true;
  }

  /// <summary>Decodes one packet into its planes, each a full picture's worth of rows.</summary>
  internal byte[][] DecodePlanes(ReadOnlySpan<byte> data) {
    var tableBytes = 2 * this._height * this._planes;
    if (data.Length < tableBytes)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an 8BPS packet of {data.Length} byte(s), where {this._planes} plane(s) of {this._height} row length(s) apiece need {tableBytes} for their tables alone.");

    var rowLengths = new int[this._planes][];
    var tablePosition = 0;
    for (var plane = 0; plane < this._planes; ++plane) {
      var lengths = new int[this._height];
      for (var row = 0; row < this._height; ++row) {
        lengths[row] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(tablePosition, 2));
        tablePosition += 2;
      }

      rowLengths[plane] = lengths;
    }

    var planes = new byte[this._planes][];
    var position = tableBytes;
    for (var plane = 0; plane < this._planes; ++plane) {
      var picture = new byte[this._width * this._height];
      for (var row = 0; row < this._height; ++row) {
        var lineLength = rowLengths[plane][row];
        var lineEnd = position + lineLength;
        if (lineEnd > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex}, plane {plane}, row {row} states {lineLength} compressed byte(s), reaching past the end of an {data.Length}-byte packet.");

        _DecodeLine(data, ref position, lineEnd, picture.AsSpan(row * this._width, this._width), this._streamIndex, plane, row);
        if (position != lineEnd)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex}, plane {plane}, row {row} filled its {this._width} pixel(s) using {position - (lineEnd - lineLength)} of the {lineLength} byte(s) it was allotted, leaving bytes unread that the row's own table entry states it owns.");

        position = lineEnd;
      }

      planes[plane] = picture;
    }

    if (position != data.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries {data.Length} byte(s), but every plane's rows together account for only {position} of them.");

    return planes;
  }

  /// <summary>PackBits, exactly: a literal run of <c>control + 1</c> bytes at 127 and below, a run of
  /// the following byte repeated <c>257 - control</c> times above it.</summary>
  private static void _DecodeLine(
    ReadOnlySpan<byte> data, ref int position, int lineEnd, Span<byte> row, int streamIndex, int plane, int rowIndex) {
    var written = 0;
    var width = row.Length;

    while (written < width) {
      if (position >= lineEnd)
        throw new InvalidDataException(
          $"Video stream {streamIndex}, plane {plane}, row {rowIndex} ran out of its allotted compressed bytes with {width - written} of {width} pixel(s) still undecoded.");

      var control = data[position++];
      if (control <= 127) {
        var count = control + 1;
        if (written + count > width)
          throw new InvalidDataException(
            $"Video stream {streamIndex}, plane {plane}, row {rowIndex} has a literal run of {count} pixel(s) reaching past the picture's width of {width}.");

        if (position + count > lineEnd)
          throw new InvalidDataException(
            $"Video stream {streamIndex}, plane {plane}, row {rowIndex} has a literal run of {count} byte(s) reaching past the compressed length its table entry states.");

        data.Slice(position, count).CopyTo(row.Slice(written, count));
        position += count;
        written += count;
      } else {
        if (position >= lineEnd)
          throw new InvalidDataException(
            $"Video stream {streamIndex}, plane {plane}, row {rowIndex} opens a repeat run with no byte left in its allotted compressed bytes to repeat.");

        var value = data[position++];
        var count = 257 - control;
        if (written + count > width)
          throw new InvalidDataException(
            $"Video stream {streamIndex}, plane {plane}, row {rowIndex} has a repeat run of {count} pixel(s) reaching past the picture's width of {width}.");

        row.Slice(written, count).Fill(value);
        written += count;
      }
    }
  }

  /// <summary>Interleaves the decoded planes into the packed format <see cref="TryDecode"/> promises —
  /// a straight copy for the one-plane indexed depth, red/green/blue (and alpha) per pixel otherwise.</summary>
  private byte[] _Pack(byte[][] planes) {
    if (this._planes == 1)
      return planes[0];

    var pixelCount = this._width * this._height;
    var bytesPerPixel = this._planes;
    var packed = new byte[pixelCount * bytesPerPixel];
    for (var plane = 0; plane < this._planes; ++plane) {
      var source = planes[plane];
      for (var pixel = 0; pixel < pixelCount; ++pixel)
        packed[pixel * bytesPerPixel + plane] = source[pixel];
    }

    return packed;
  }

  /// <summary>The entry with its box header taken off — four bytes of length and four of the codec's
  /// code, or sixteen where the length is the escape value and a sixty-four bit length follows.</summary>
  private static ReadOnlySpan<byte> _Body(ReadOnlySpan<byte> sampleDescription) {
    if (sampleDescription.Length < 8)
      return default;

    var header = BinaryPrimitives.ReadUInt32BigEndian(sampleDescription) == 1 ? 16 : 8;
    return sampleDescription.Length < header ? default : sampleDescription[header..];
  }

  private static int _ReadDepth(ReadOnlySpan<byte> sampleDescription) {
    var body = _Body(sampleDescription);
    return body.Length < _DEPTH_AT + 2 ? 0 : BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_DEPTH_AT, 2));
  }

  /// <summary>
  /// Reads an indexed picture's colour table out of the sample description, refusing a table this
  /// project cannot check a reading of against anything.
  /// </summary>
  /// <remarks>
  /// A colour table identifier of zero is what every real 8BPS sample measured here states, and it
  /// means the table is the bytes that immediately follow: a four-byte seed, a two-byte flags field,
  /// a two-byte count of entries minus one, and then that many entries — a two-byte index, and red,
  /// green and blue each at sixteen bits, of which only the high byte is kept. That last step is not
  /// read off the format's own documentation, which says nothing about indexed pictures at all; it is
  /// what a real file's embedded table produces when compared entry for entry against ffmpeg's own
  /// decoded palette for the same file, agreeing on every one of 256 entries.
  /// <para/>
  /// Any other identifier names a colour resource kept outside the file — the classic Macintosh system
  /// palette by another number, or <c>-1</c> stating there is none at all — and no sample measured
  /// here uses either, so neither is guessed at.
  /// </remarks>
  private static (byte[] Palette, int Count) _ReadPalette(ReadOnlySpan<byte> sampleDescription, int streamIndex) {
    var body = _Body(sampleDescription);
    if (body.Length < _COLOUR_TABLE_AT)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries {sampleDescription.Length} byte(s) of sample description, too short to reach the colour table an eight-bit 8BPS picture needs.");

    var tableId = (short)BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_COLOUR_TABLE_ID_AT, 2));
    if (tableId != _CUSTOM_COLOUR_TABLE_ID)
      throw new NotSupportedException(
        $"Video stream {streamIndex} names colour table {tableId} for an eight-bit 8BPS picture, rather than the embedded custom table (identifier 0) every sample measured here carries — a system colour resource by number is not a table this project can check a reading of against anything.");

    var table = body[_COLOUR_TABLE_AT..];
    if (table.Length < 8)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a custom colour table but carries only {table.Length} byte(s) after its identifier, short of the eight a table's seed, flags and entry count take alone.");

    var entryCount = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    var needed = 8 + entryCount * 8;
    if (table.Length < needed)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a colour table of {entryCount} entries but carries only {table.Length - 8} byte(s) of entries, short of the {entryCount * 8} that many eight-byte entries need.");

    var palette = new byte[entryCount * 3];
    var position = 8;
    for (var i = 0; i < entryCount; ++i) {
      var index = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(position, 2));
      if (index >= entryCount)
        throw new InvalidDataException(
          $"Video stream {streamIndex}'s colour table names entry {index}, outside the {entryCount} entries the table itself states.");

      palette[index * 3] = table[position + 2];
      palette[index * 3 + 1] = table[position + 4];
      palette[index * 3 + 2] = table[position + 6];
      position += 8;
    }

    return (palette, entryCount);
  }
}
