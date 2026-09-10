using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.FlicVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Autodesk FLIC (<c>FLIC</c>): eight-bit palette updates and whole palettised pictures over
/// the same persistent canvas <see cref="FlicVideoDecoder"/> reads.
/// </summary>
/// <remarks>
/// Written from the published FLIC bitstream description rather than from an encoder implementation.
/// The coding is lossless and takes <see cref="PixelFormat.Indexed8"/> pictures only: choosing a 256
/// colour approximation for a true-colour picture is quantisation, and silently deciding which
/// colours survive is outside a codec writer's job.
/// <para/>
/// A FLIC palette belongs to the frame stream rather than to a container header, so it may change at
/// any frame. The first packet states all 256 entries through <c>FLI_COLOR256</c>; later packets state
/// one span covering every entry that changed. Entries beyond a picture's declared palette are black,
/// and a palette index outside that declared palette is refused rather than decoded through an
/// invented colour.
/// <para/>
/// Pictures are deliberately written through the unambiguous common subset. A black canvas uses
/// <c>FLI_BLACK</c>; every other changed canvas uses <c>FLI_BRUN</c>, with replicated and literal runs
/// split at the signed-byte limit; and an unchanged canvas carries no picture sub-chunk at all. The
/// latter is a real FLIC spelling of "leave the previous canvas alone", and lets palette-only frames
/// change colours without rewriting the indices.
/// <para/>
/// <c>FLI_COPY</c> is not emitted. The published format describes it as exactly width*height bytes,
/// while FFmpeg's eight-bit decoder accepts DWORD-padded rows. <c>FLI_BRUN</c> has no such competing
/// layout and is the whole-frame coding used by real Animator files, so interoperability does not need
/// to pick a side in that disagreement.
/// </remarks>
public sealed class FlicVideoEncoder : IVideoCodecEncoder<FlicVideoEncoder> {

  private static readonly CodecTag _FLIC = CodecTag.FromCharacters("FLIC");

  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _LONGEST_BRUN_RUN = 127;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;

  private readonly byte[] _previousPalette = new byte[_PALETTE_BYTES];
  private byte[]? _previousPixels;

  private FlicVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
  }

  public static string CodecName => "FLIC";

  public static CodecTag Codec => _FLIC;

  public static FlicVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("FLIC can only encode a video stream.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException(
        $"A FLIC encoder needs a picture size fitting its unsigned 16-bit header fields; "
        + $"{stream.Width}x{stream.Height} was supplied.");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is more pixels than a FLIC frame can hold.");
    if (stream.BitsPerPixel is not (0 or 8))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. FLIC under the standard "
        + "0xAF11/0xAF12 headers is palettised eight-bit and nothing else is written.");

    return new(stream);
  }

  /// <summary>Codes one picture, preserving both its palette and its palette indices exactly.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"FLIC geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"FLIC codes palette indices and takes only Indexed8 pictures; a {frame.Format} picture would have to be "
        + "quantised first, and which 256 colours to keep is not this codec's decision.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var palette = this._Palette(frame);
    var pixels = frame.PixelData.AsSpan(0, this._width * this._height);
    this._ValidateIndices(pixels, frame.PaletteCount);

    var paletteChunk = this._PaletteChunk(palette);
    var pictureChunk = this._PictureChunk(pixels);
    var data = _Join(paletteChunk, pictureChunk);

    palette.AsSpan().CopyTo(this._previousPalette);
    this._previousPixels = pixels.ToArray();

    packet = new(
      StreamIndex: this._requested.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: pictureChunk != null);
    return true;
  }

  /// <summary>The one eight-bit FLIC stream a <see cref="FliWriter"/> needs described.</summary>
  public MediaStreamInfo DescribeStream() => new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _FLIC,
    Handler = _FLIC,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 8,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>Returns a complete 256-entry RGB palette, padding undeclared entries with black.</summary>
  private byte[] _Palette(RawImage frame) {
    var sourcePalette = frame.Palette;
    if (sourcePalette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException(
        "A palettised picture without a palette cannot be coded: FLIC frames hold palette indices and palette "
        + "updates, and there are no colours to state.");
    if (frame.PaletteCount > _PALETTE_ENTRIES)
      throw new InvalidDataException(
        $"The picture states {frame.PaletteCount} palette entries, but an eight-bit FLIC palette holds at most 256.");

    var needed = frame.PaletteCount * 3;
    if (sourcePalette.Length < needed)
      throw new InvalidDataException(
        $"The picture states a palette of {frame.PaletteCount} entries but carries {sourcePalette.Length / 3}.");

    var result = new byte[_PALETTE_BYTES];
    sourcePalette.AsSpan(0, needed).CopyTo(result);
    return result;
  }

  private void _ValidateIndices(ReadOnlySpan<byte> pixels, int paletteCount) {
    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {pixels[i]} and the picture declares "
          + $"{paletteCount} palette entries; no colour exists for that index.");
  }

  // ============================================================================================
  // Palette updates
  // ============================================================================================

  private byte[]? _PaletteChunk(ReadOnlySpan<byte> palette) {
    if (this._previousPixels == null)
      return _Color256Chunk(palette, 0, _PALETTE_ENTRIES);

    var first = -1;
    var last = -1;
    for (var entry = 0; entry < _PALETTE_ENTRIES; ++entry) {
      var at = entry * 3;
      if (palette.Slice(at, 3).SequenceEqual(this._previousPalette.AsSpan(at, 3)))
        continue;

      first = first < 0 ? entry : first;
      last = entry;
    }

    return first < 0 ? null : _Color256Chunk(palette, first, last - first + 1);
  }

  private static byte[] _Color256Chunk(ReadOnlySpan<byte> palette, int first, int count) {
    var payload = new byte[4 + count * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 1); // one packet
    payload[2] = (byte)first;
    payload[3] = count == _PALETTE_ENTRIES ? (byte)0 : checked((byte)count);
    palette.Slice(first * 3, count * 3).CopyTo(payload.AsSpan(4));
    return _Chunk(FliChunkType.COLOR256, payload);
  }

  // ============================================================================================
  // Whole pictures
  // ============================================================================================

  private byte[]? _PictureChunk(ReadOnlySpan<byte> pixels) {
    if (this._previousPixels != null && pixels.SequenceEqual(this._previousPixels))
      return null;

    if (_IsAllZero(pixels))
      return _Chunk(FliChunkType.BLACK, []);

    return _Chunk(FliChunkType.BRUN, this._Brun(pixels));
  }

  /// <summary>
  /// Encodes every row as byte runs. Positive counts replicate one index; negative counts copy that
  /// many literal indices. The row packet-count byte is deliberately zero: the original format keeps
  /// the field but its decoder is specified to ignore it, which is necessary once a 65535-pixel row
  /// needs more than 255 packets.
  /// </summary>
  private byte[] _Brun(ReadOnlySpan<byte> pixels) {
    using var output = new MemoryStream();

    for (var y = 0; y < this._height; ++y) {
      output.WriteByte(0); // packet count is a held-over field and is not used by the decoder
      var row = pixels.Slice(y * this._width, this._width);
      var x = 0;

      while (x < this._width) {
        var repeated = 1;
        while (repeated < _LONGEST_BRUN_RUN && x + repeated < this._width && row[x + repeated] == row[x])
          ++repeated;

        if (repeated >= 2) {
          output.WriteByte((byte)repeated);
          output.WriteByte(row[x]);
          x += repeated;
          continue;
        }

        var literalStart = x++;
        while (x < this._width && x - literalStart < _LONGEST_BRUN_RUN) {
          if (x + 1 < this._width && row[x] == row[x + 1])
            break;
          ++x;
        }

        var literalLength = x - literalStart;
        output.WriteByte(unchecked((byte)-literalLength));
        output.Write(row.Slice(literalStart, literalLength));
      }
    }

    return output.ToArray();
  }

  private static bool _IsAllZero(ReadOnlySpan<byte> values) {
    foreach (var value in values)
      if (value != 0)
        return false;

    return true;
  }

  // ============================================================================================
  // Sub-chunk framing
  // ============================================================================================

  private static byte[] _Chunk(ushort type, ReadOnlySpan<byte> payload) {
    var result = new byte[6 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), type);
    payload.CopyTo(result.AsSpan(6));
    return result;
  }

  private static byte[] _Join(byte[]? first, byte[]? second) {
    if (first == null)
      return second ?? [];
    if (second == null)
      return first;

    var result = new byte[first.Length + second.Length];
    first.CopyTo(result, 0);
    second.CopyTo(result, first.Length);
    return result;
  }
}
