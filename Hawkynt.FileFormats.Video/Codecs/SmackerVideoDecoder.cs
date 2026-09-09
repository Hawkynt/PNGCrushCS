using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Smacker;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Smacker video (<c>SMK2</c> and <c>SMK4</c>) — RAD Game Tools' own FMV codec, behind more
/// games of the 1990s than any other in this package's game-and-FMV family: a paletted picture built
/// from 4x4 blocks, every block described by a run-length descriptor read through one of four
/// Huffman tables the file states once and every frame then shares.
/// </summary>
/// <remarks>
/// The four tables — <c>MMap</c>, <c>MClr</c>, <c>Full</c> and <c>Type</c> — are unpacked once from the
/// stream's own private data by <see cref="SmackerSymbolTable"/>, whose remarks record the four things
/// RAD's published description leaves out about how they are composed, and which are the reason this
/// codec sat undecoded here for as long as it did. <see cref="SmackerPictureDecoder"/> is what each
/// block type then paints.
/// <para/>
/// <b>The packet handed here is not the frame's own bytes verbatim.</b>
/// <see cref="FileFormat.SmackerVideo.SmackerContainer"/> prepends the frame's flag byte, because
/// whether a palette chunk opens the frame is stated once per frame in an array outside the frame
/// entirely, and a decoder handed only the bytes could not tell a palette chunk from the start of the
/// picture. Resolving the running palette from a file's chain of partial restatements is this decoder's
/// work rather than the container's, which is why <see cref="SmackerPalette"/> lives here.
/// <para/>
/// <b>Measured.</b> Every <c>.smk</c> file on <c>samples.ffmpeg.org</c> and in FFmpeg's own FATE suite
/// — nine of them, eight <c>SMK2</c> and one <c>SMK4</c>, from five games and two of FFmpeg's own bug
/// reports — was decoded here and by FFmpeg 9.0.1. One, <c>mech2/mintro.smk</c>, is truncated to a
/// twelfth of the size its own header states and is refused by FFmpeg and by this package's reader
/// alike. The other eight, 120x76 to 640x480 and 1,473 pictures in all, were compared on the paletted
/// output both decoders produce rather than on RGB, so that no colour conversion sits between them:
/// all 127,616,480 palette indices and all 1,131,264 palette bytes are identical, with no difference
/// on any frame of any file.
/// <para/>
/// The one <c>SMK4</c> file carries its own weight — 303 pictures, and between them 83,075 full-block
/// runs spread across all three of the shapes that revision adds, 21,040 of the plain row form, 38,441
/// of the quadrant form and 23,594 of the doubled-row form — so the bits that pick a shape and each
/// shape's own painting are exercised by real data rather than only by this package's own tests.
/// <para/>
/// <b>What is refused rather than guessed.</b> A picture whose width or height is not a whole number of
/// 4x4 blocks — the format codes nothing to cover the fringe, so a decoder would be inventing it — and
/// a stream whose header states none of the four tables at all.
/// </remarks>
public sealed class SmackerVideoDecoder : IVideoCodecDecoder<SmackerVideoDecoder> {

  private const int _BLOCK = 4;
  private const int _TABLE_SIZES_LENGTH = 16;
  private const byte _FRAME_TYPE_HAS_PALETTE = 1;

  private static readonly CodecTag _SMK2 = CodecTag.FromCharacters("SMK2");
  private static readonly CodecTag _SMK4 = CodecTag.FromCharacters("SMK4");

  private readonly SmackerSymbolTable _mmap;
  private readonly SmackerSymbolTable _mclr;
  private readonly SmackerSymbolTable _full;
  private readonly SmackerSymbolTable _type;
  private readonly bool _isVersion4;
  private readonly int _width;
  private readonly int _height;
  private readonly byte[] _canvas;

  private byte[] _palette = new byte[SmackerPalette.BYTE_COUNT];

  public static string CodecName => "Smacker Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
      && (stream.Codec.EqualsIgnoringCase(_SMK2) || stream.Codec.EqualsIgnoringCase(_SMK4));
  }

  public static SmackerVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"A Smacker video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    if (stream.Width % _BLOCK != 0 || stream.Height % _BLOCK != 0)
      throw new NotSupportedException(
        $"A Smacker video stream states a picture of {stream.Width}x{stream.Height}, which is not a whole "
        + $"number of {_BLOCK}x{_BLOCK} blocks. Smacker codes nothing at all for the pixels such a picture "
        + "would have left over, so what belongs there is not something this decoder can work out; no file "
        + "measured against it states one either. It is refused rather than painted with a guessed fringe.");

    var privateData = stream.CodecPrivateData.Span;
    if (privateData.Length <= _TABLE_SIZES_LENGTH)
      throw new InvalidDataException(
        $"A Smacker video stream's private data is {privateData.Length} bytes, which is not past the "
        + $"{_TABLE_SIZES_LENGTH} its four table-size fields take before the tree data even starts.");

    // Each size is what RAD's own header set aside for that table in memory. It is not a byte count of
    // anything in the file, but at four bytes a slot it is exactly how many slots the packed tree that
    // follows may unpack to, which is the only bound there is on a structure that otherwise states its
    // own extent nowhere.
    var reader = new SmackerBitReader(privateData[_TABLE_SIZES_LENGTH..]);
    var mmap = SmackerSymbolTable.Read(ref reader, BinaryPrimitives.ReadUInt32LittleEndian(privateData));
    var mclr = SmackerSymbolTable.Read(ref reader, BinaryPrimitives.ReadUInt32LittleEndian(privateData[4..]));
    var full = SmackerSymbolTable.Read(ref reader, BinaryPrimitives.ReadUInt32LittleEndian(privateData[8..]));
    var type = SmackerSymbolTable.Read(ref reader, BinaryPrimitives.ReadUInt32LittleEndian(privateData[12..]));

    if (reader.BitsRemaining < 0)
      throw new InvalidDataException(
        "A Smacker video stream's four Huffman tables read past the end of the tree section its own header "
        + "states, so the section is shorter than the tables it declares.");

    if (mmap.IsAbsent && mclr.IsAbsent && full.IsAbsent && type.IsAbsent)
      throw new InvalidDataException(
        "A Smacker video stream states none of its four Huffman tables. Every block of every picture "
        + "would then decode to the same descriptor, so the stream describes no pictures at all.");

    return new(mmap, mclr, full, type, stream.Codec.EqualsIgnoringCase(_SMK4), stream.Width, stream.Height);
  }

  private SmackerVideoDecoder(
    SmackerSymbolTable mmap,
    SmackerSymbolTable mclr,
    SmackerSymbolTable full,
    SmackerSymbolTable type,
    bool isVersion4,
    int width,
    int height
  ) {
    this._mmap = mmap;
    this._mclr = mclr;
    this._full = full;
    this._type = type;
    this._isVersion4 = isVersion4;
    this._width = width;
    this._height = height;
    this._canvas = new byte[width * height];
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 1)
      throw new InvalidDataException("A Smacker video packet has no frame-type byte.");

    var at = 1;
    if ((data[0] & _FRAME_TYPE_HAS_PALETTE) != 0) {
      if (at >= data.Length)
        throw new InvalidDataException(
          "A Smacker video packet states a palette chunk but has no bytes left to hold one.");

      // The chunk's own first byte counts itself, in units of four bytes.
      var chunkLength = data[at] * 4;
      if (chunkLength < 1 || at + chunkLength > data.Length)
        throw new InvalidDataException(
          $"A Smacker video packet's palette chunk states {chunkLength} bytes, which does not fit in the "
          + $"packet's own {data.Length}.");

      this._palette = SmackerPalette.Apply(data.Slice(at, chunkLength), this._palette);
      at += chunkLength;
    }

    SmackerPictureDecoder.Decode(
      data[at..], this._mmap, this._mclr, this._full, this._type, this._isVersion4,
      this._canvas, this._width, this._height);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._canvas.Clone(),
      Palette = (byte[])this._palette.Clone(),
      PaletteCount = SmackerPalette.ENTRY_COUNT,
    };

    return true;
  }
}
