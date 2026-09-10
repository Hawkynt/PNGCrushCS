using System;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Encodes one baseline H.263 intra picture: picture header, macroblocks and blocks.
/// </summary>
/// <remarks>
/// This writer deliberately emits no optional group-of-block headers. H.263 clause 4.2.1 permits
/// headers for GOBs after the first to be empty depending on encoder strategy; with none present the
/// macroblock run simply continues across every group boundary, which the decoder beside this already
/// handles. No annex mode is signalled and every macroblock is type INTRA at one fixed picture
/// quantiser.
/// </remarks>
internal sealed class H263PictureEncoder {

  /// <summary>PSC plus GN=0, ITU-T H.263 clause 5.1.1: twenty-two bits whose numeric value is one.</summary>
  private const int _PICTURE_START_CODE = 1;
  private const int _PICTURE_START_CODE_LENGTH = 22;

  private readonly int _sourceFormat;
  private readonly int _temporalReference;
  private readonly int _quantiser;
  private readonly H263Frame _source;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly H263BitWriter _writer = new();

  internal H263PictureEncoder(int sourceFormat, int temporalReference, int quantiser, H263Frame source) {
    if (sourceFormat is < 1 or > 5)
      throw new ArgumentOutOfRangeException(nameof(sourceFormat));

    if (quantiser is < 1 or > 31)
      throw new ArgumentOutOfRangeException(nameof(quantiser));

    this._sourceFormat = sourceFormat;
    this._temporalReference = temporalReference & 0xFF;
    this._quantiser = quantiser;
    this._source = source ?? throw new ArgumentNullException(nameof(source));
    this._macroblockWidth = source.LumaWidth / 16;
    this._macroblockHeight = source.LumaHeight / 16;
  }

  /// <summary>Encodes the complete picture and returns its byte-aligned elementary-stream payload.</summary>
  internal byte[] Encode() {
    this._WritePictureHeader();

    Span<int> samples = stackalloc int[64];
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> direct = stackalloc int[6];
    Span<bool> coded = stackalloc bool[6];

    var count = this._macroblockWidth * this._macroblockHeight;
    for (var address = 0; address < count; ++address) {
      var luminancePattern = 0;
      var chrominancePattern = 0;

      for (var index = 0; index < 6; ++index) {
        this._ReadSource(samples, address, index);
        var block = levels.Slice(index * 64, 64);
        coded[index] = H263BlockEncoder.QuantiseIntra(samples, this._quantiser, block, out direct[index]);

        if (!coded[index])
          continue;

        if (index < 4)
          luminancePattern |= 1 << (3 - index);
        else
          chrominancePattern |= 1 << (5 - index);
      }

      this._writer.WriteCode(H263VlcWriter.IntraMacroblockType(chrominancePattern));
      this._writer.WriteCode(H263VlcWriter.LuminancePattern(luminancePattern));

      for (var index = 0; index < 6; ++index)
        H263BlockEncoder.WriteIntra(
          this._writer, direct[index], levels.Slice(index * 64, 64), coded[index]);
    }

    return this._writer.ToArray();
  }

  private void _WritePictureHeader() {
    this._writer.Write(_PICTURE_START_CODE, _PICTURE_START_CODE_LENGTH);
    this._writer.Write(this._temporalReference, 8);

    // PTYPE (5.1.3): the two fixed discriminator bits, three display-only flags clear, one of Table 5's
    // five source formats, an I-picture, and every optional baseline mode clear.
    this._writer.WriteBit(1);
    this._writer.WriteBit(0);
    this._writer.Write(0, 3);
    this._writer.Write(this._sourceFormat, 3);
    this._writer.WriteBit(0);             // PICTURE CODING TYPE: intra
    this._writer.Write(0, 4);             // UMV, SAC, advanced prediction, PB-frames

    this._writer.Write(this._quantiser, 5);
    this._writer.WriteBit(0);             // CPM
    this._writer.WriteBit(0);             // PEI: no extra picture information
  }

  private void _ReadSource(scoped Span<int> samples, int address, int index) {
    var (plane, width, _) = this._source.PlaneOf(index);
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var left = index < 4 ? column * 16 + (index & 1) * 8 : column * 8;
    var top = index < 4 ? row * 16 + (index >> 1) * 8 : row * 8;

    for (var y = 0; y < 8; ++y) {
      var source = (top + y) * width + left;
      var target = y * 8;
      for (var x = 0; x < 8; ++x)
        samples[target + x] = plane[source + x];
    }
  }
}
