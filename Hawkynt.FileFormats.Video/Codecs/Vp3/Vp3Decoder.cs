using System;
using System.IO;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Decodes a VP3 stream, one coded frame at a time, keeping the two reference frames a later frame
/// predicts from.
/// </summary>
/// <remarks>
/// A frame is read in six passes over the whole picture before a single pixel is reconstructed: which
/// blocks are coded, what mode each macro block uses, what its motion vector is, and then sixty-four
/// passes for the coefficients, followed by undoing the DC prediction. Nothing can be reconstructed
/// earlier because none of those passes is per-block — the coded flags are run-length coded across
/// the frame, an end-of-block run crosses hundreds of blocks, and the DC coefficient of a block is a
/// residual from its neighbours.
/// <para/>
/// Two reference frames are kept: the previous frame, whatever kind it was, and the golden frame,
/// which is the most recent intra frame. Both are the reconstruction after the loop filter has run —
/// Theora unified this, where the original VP3 decoder padded the previous frame before filtering and
/// the golden frame after, so the two could differ in their edge padding even when they held the same
/// picture. Nothing here pads at all; <see cref="Vp3Prediction"/> clamps coordinates instead, which
/// gives the samples an edge-replicated border would give and gives them for any distance.
/// <para/>
/// Frame sizes come from the container. VP3 relies on it for them — its own frame header carries
/// bits that appear to restate the size, but which no stream this was tested against varies — so a
/// size that is not a multiple of sixteen is rounded up to whole macro blocks here, and the crop back
/// to the stated size happens on the way out.
/// </remarks>
internal sealed class Vp3Decoder {

  /// <summary>
  /// How many bits of a packet may go unread before the frame is refused.
  /// </summary>
  /// <remarks>
  /// A frame that was read out of step with the bitstream almost never gets this far — its tokens
  /// stop accounting for the coefficients of every coded block long before the end — so this is a
  /// backstop and is set loosely on purpose. Real VP3 files leave more than the padding to the next
  /// byte: over the five hundred and fourteen frames this was measured against, the most any frame
  /// left was thirty-one bits, which is the encoder flushing whatever was in its register and not
  /// something a decoder can predict. Refusing at anything near that would refuse frames that decode
  /// perfectly; a frame read out of step leaves thousands.
  /// </remarks>
  private const int _MAXIMUM_UNREAD_BITS = 128;

  private readonly Vp3Geometry _geometry;
  private readonly int _width;
  private readonly int _height;

  private readonly bool[] _coded;
  private readonly bool[] _superBlockPartial;
  private readonly bool[] _superBlockWhole;
  private readonly bool[] _blockFlags;
  private readonly byte[] _modes;
  private readonly int[] _modeAlphabet = new int[8];
  private readonly sbyte[] _motionX;
  private readonly sbyte[] _motionY;
  private readonly short[] _coefficients;
  private readonly byte[] _counts;
  private readonly byte[] _positions;

  private readonly int[][] _quantisers = [
    new int[64], new int[64], new int[64], new int[64], new int[64], new int[64],
  ];

  private readonly short[] _dequantised = new short[64];
  private readonly short[] _residual = new short[64];
  private readonly int[] _predictor = new int[64];

  private Vp3Frame _previous;
  private Vp3Frame _golden;
  private Vp3Frame _current;
  private bool _started;

  /// <summary>The picture size the container states, which is what a decoded frame is cropped to.</summary>
  internal int Width => this._width;

  internal int Height => this._height;

  /// <summary>How many bits of the last packet went unread, which is its encoder's flush.</summary>
  internal int UnreadBits { get; private set; }

  internal Vp3Decoder(int width, int height) {
    if (width <= 0 || height <= 0)
      throw new NotSupportedException(
        $"This VP3 stream states a picture of {width}×{height}. VP3 carries no picture size of its own, so "
        + "the container has to state one, and this container states one with no area.");

    this._width = width;
    this._height = height;

    var columns = (width + 15) / 16;
    var rows = (height + 15) / 16;
    var geometry = new Vp3Geometry(columns, rows);
    this._geometry = geometry;

    this._coded = new bool[geometry.BlockCount];
    this._superBlockPartial = new bool[geometry.SuperBlockCount];
    this._superBlockWhole = new bool[geometry.SuperBlockCount];
    this._blockFlags = new bool[geometry.BlockCount];
    this._modes = new byte[geometry.MacroblockCount];
    this._motionX = new sbyte[geometry.BlockCount];
    this._motionY = new sbyte[geometry.BlockCount];
    this._coefficients = new short[geometry.BlockCount * 64];
    this._counts = new byte[geometry.BlockCount];
    this._positions = new byte[geometry.BlockCount];

    this._previous = new(columns, rows);
    this._golden = new(columns, rows);
    this._current = new(columns, rows);
  }

  /// <summary>
  /// Decodes one packet, which for VP3 is exactly one frame, and returns the reconstruction.
  /// </summary>
  /// <remarks>
  /// A packet of no bytes is a frame in which nothing changed, and is treated as an inter frame with
  /// no coded blocks — which reconstructs to a copy of the previous frame. That is the one case where
  /// handing back the previous picture is the answer and not a failure to decode.
  /// <para/>
  /// <b>The frame returned belongs to the decoder and is reused.</b> Three buffers rotate — the frame
  /// just decoded, the one before it and the golden frame — so what comes back here stays intact
  /// across the next call and is overwritten by the one after that. A caller that wants to keep a
  /// frame must copy the samples out before then, which is what <see cref="Vp3VideoDecoder"/> does by
  /// converting to RGB straight away. A caller that collects the returned objects and reads them later
  /// will find every one of them holding whichever picture was decoded last, which looks like a
  /// decoder that breaks partway through a stream rather than like a caller holding a buffer it does
  /// not own.
  /// </remarks>
  internal Vp3Frame Decode(ReadOnlyMemory<byte> packet) {
    var geometry = this._geometry;
    bool isIntra;
    int quantisationIndex;

    if (packet.Length == 0) {
      if (!this._started)
        throw new InvalidDataException(
          "This VP3 stream begins with an empty packet, which states that nothing changed since a frame that "
          + "was never sent.");

      isIntra = false;
      quantisationIndex = 63;
      Array.Clear(this._coded);
      Array.Clear(this._modes);
      Array.Clear(this._coefficients);
      Array.Clear(this._counts);
    } else {
      var reader = new Vp3BitReader(packet);
      var header = Vp3FrameHeader.Read(reader);
      isIntra = header.IsIntra;
      quantisationIndex = header.QuantisationIndex;

      if (!this._started)
        Vp3FrameHeader.RequireIntraFirst(isIntra);

      if (isIntra) {
        Vp3BlockFlags.All(this._coded, geometry.BlockCount);
        Vp3ModeReader.AllIntra(this._modes, geometry.MacroblockCount);
      } else {
        Vp3BlockFlags.Read(
          reader, geometry, this._coded, this._superBlockPartial, this._superBlockWhole, this._blockFlags);
        Vp3ModeReader.ReadModes(reader, geometry, this._coded, this._modes, this._modeAlphabet);
        Vp3ModeReader.ReadMotionVectors(
          reader, geometry, this._coded, this._modes, this._motionX, this._motionY);
      }

      Vp3TokenReader.Read(reader, geometry, this._coded, this._coefficients, this._counts, this._positions);
      Vp3DcPrediction.Undo(geometry, this._coded, this._modes, this._coefficients);

      this.UnreadBits = reader.Length - reader.Position;
      if (this.UnreadBits > _MAXIMUM_UNREAD_BITS)
        throw new InvalidDataException(
          $"A VP3 frame left {this.UnreadBits} bits of its {packet.Length}-byte packet unread. A frame ends at "
          + "its encoder's flush, within a byte or two of the end, so this much left over means the frame was "
          + "read out of step with the bitstream and the picture it produced is not the one that was coded.");
    }

    this._Reconstruct(quantisationIndex);
    Vp3LoopFilter.Apply(this._current, geometry, this._coded, quantisationIndex);

    if (isIntra)
      this._golden.CopyFrom(this._current);

    (this._previous, this._current) = (this._current, this._previous);
    this._started = true;
    return this._previous;
  }

  /// <summary>
  /// Builds the frame block by block: predictor plus residual, clamped (Section 7.9.4).
  /// </summary>
  private void _Reconstruct(int quantisationIndex) {
    var geometry = this._geometry;

    for (var quantisationType = 0; quantisationType < 2; ++quantisationType)
    for (var plane = 0; plane < 3; ++plane)
      Vp3Quantisation.Build(
        quantisationType, plane, quantisationIndex, this._quantisers[quantisationType * 3 + plane]);

    for (var block = 0; block < geometry.BlockCount; ++block) {
      var plane = geometry.BlockPlane[block];
      var planeWidth = geometry.PlaneWidth[plane];
      var planeHeight = geometry.PlaneHeight[plane];
      var originX = geometry.BlockColumn[block] * 8;
      var originY = geometry.BlockRow[block] * 8;
      var samples = this._current.Plane(plane);
      var predictor = this._predictor;
      var residual = this._residual;

      if (this._coded[block]) {
        var mode = this._modes[geometry.MacroblockOfBlock[block]];
        var reference = Vp3Tables.ReferenceOfMode[mode];
        var quantisationType = mode == Vp3ModeReader.INTRA ? 0 : 1;
        var quantiser = this._quantisers[quantisationType * 3 + plane];

        if (reference == 0)
          Vp3Prediction.Intra(predictor);
        else
          Vp3Prediction.Inter(
            (reference == 1 ? this._previous : this._golden).Plane(plane),
            planeWidth, planeHeight, originX, originY,
            this._motionX[block], this._motionY[block], plane == 0 ? 2 : 4, predictor);

        var at = block * 64;
        if (this._counts[block] < 2) {
          // Nothing but a DC coefficient, so the whole block is one value and the transform is
          // skipped. The arithmetic is not the transform's: no intermediate truncation, and a
          // different rounding.
          var flat = (short)((this._coefficients[at] * quantiser[0] + 15) >> 5);
          for (var i = 0; i < 64; ++i)
            residual[i] = flat;
        } else {
          var dequantised = this._dequantised;
          dequantised[0] = (short)(this._coefficients[at] * quantiser[0]);
          for (var coefficient = 1; coefficient < 64; ++coefficient)
            dequantised[coefficient] =
              (short)(this._coefficients[at + Vp3Tables.ZigZag[coefficient]] * quantiser[coefficient]);

          Vp3InverseDct.Transform(dequantised, residual);
        }
      } else {
        // An uncoded block is the co-located block of the previous frame, unchanged.
        Vp3Prediction.Inter(
          this._previous.Plane(plane), planeWidth, planeHeight, originX, originY, 0, 0, 2, predictor);
        Array.Clear(residual);
      }

      for (var row = 0; row < 8; ++row) {
        var at = (originY + row) * planeWidth + originX;
        for (var column = 0; column < 8; ++column) {
          var value = predictor[row * 8 + column] + residual[row * 8 + column];
          samples[at + column] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
        }
      }
    }
  }
}
