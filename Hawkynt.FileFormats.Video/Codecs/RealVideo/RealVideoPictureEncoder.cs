using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.RealVideo;

/// <summary>Encodes one intra-coded RealVideo 1 picture over the shared H.263 macroblock layer.</summary>
internal sealed class RealVideoPictureEncoder {

  private static readonly H263CodeTable _IntraMacroblockType = new(H263VlcTables.IntraMacroblockType);
  private static readonly H263CodeTable _LuminancePattern = new(H263VlcTables.LuminancePattern);

  private readonly H263Frame _source;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _quantiser;

  internal RealVideoPictureEncoder(H263Frame source, int quantiser) {
    this._source = source;
    this._macroblockWidth = source.LumaWidth / 16;
    this._macroblockHeight = source.LumaHeight / 16;
    this._quantiser = quantiser;
  }

  internal byte[] Encode() {
    var macroblockCount = this._macroblockWidth * this._macroblockHeight;
    var writer = new H263BitWriter();

    // RealVideo 1 picture header: marker, I-picture, no PB frame, quantiser, explicit run position,
    // run length and the three reserved/ignored bits. The reference encoder writes the same shape.
    writer.WriteBit(1);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.Write(this._quantiser, 5);
    writer.Write(0, 6);
    writer.Write(0, 6);
    writer.Write(macroblockCount, 12);
    writer.Write(0, 3);

    for (var address = 0; address < macroblockCount; ++address)
      this._WriteMacroblock(writer, address);

    return writer.ToArray();
  }

  private void _WriteMacroblock(H263BitWriter writer, int address) {
    Span<int> samples = stackalloc int[64];
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> direct = stackalloc int[6];

    var pattern = 0;
    for (var block = 0; block < 6; ++block) {
      this._ReadBlock(address, block, samples);
      var blockLevels = levels.Slice(block * 64, 64);
      if (H263BlockEncoder.QuantiseIntra(samples, this._quantiser, blockLevels, out direct[block]))
        pattern |= 1 << (5 - block);
    }

    var chromaPattern = pattern & 0x3;
    var luminancePattern = pattern >> 2;
    writer.WriteCode(_IntraMacroblockType[3 * 4 + chromaPattern]);
    writer.WriteCode(_LuminancePattern[luminancePattern]);

    for (var block = 0; block < 6; ++block)
      H263BlockEncoder.WriteIntra(
        writer,
        direct[block],
        levels.Slice(block * 64, 64),
        (pattern & (1 << (5 - block))) != 0);
  }

  private void _ReadBlock(int address, int block, Span<int> samples) {
    var (plane, stride, _) = this._source.PlaneOf(block);
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var left = block < 4 ? column * 16 + (block & 1) * 8 : column * 8;
    var top = block < 4 ? row * 16 + (block >> 1) * 8 : row * 8;

    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[(top + y) * stride + left + x];
  }
}