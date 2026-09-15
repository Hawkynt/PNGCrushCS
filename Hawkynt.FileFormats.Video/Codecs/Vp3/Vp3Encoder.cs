using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Vp3;

/// <summary>Encodes VP3.1 intra and inter frames.</summary>
/// <remarks>
/// A group opens with an intra frame and continues with inter frames predicted from the frame before
/// them, which is read back out of a decoder this encoder drives with its own output rather than taken
/// from the source: the two differ by the quantiser's loss, and an encoder that ignores the difference
/// is right on its first inter frame and a little further out on every one after.
/// <para/>
/// Three of VP3's coding choices are deliberately the simple ones, and each is ordinary VP3 rather
/// than a subset a decoder has to tolerate. Coefficient codebook zero is used throughout. Modes and
/// motion vectors are written in their literal forms -- the three-bit mode and the five-bits-and-a-sign
/// component -- which the decoder selects on a flag it reads before either field. And whether a block
/// is coded is decided a whole super block at a time.
/// <para/>
/// That last one is not only simplicity. The block-level pass is run-coded with runs that alternate and
/// have no escape, so a run longer than the table can state cannot be split into two of the same value;
/// deciding per super block leaves that pass empty and the question does not arise. The cost is that a
/// super block with one moving block in it codes all sixteen, which on a codec whose frames are mostly
/// untouched background is a small price for a whole class of unwritable stream.
/// <para/>
/// The golden frame is never referenced: every inter macro block predicts from the previous frame, with
/// or without a vector. Modes that reach for the golden frame, the two that reuse an earlier vector and
/// the four-vector mode are all readable and none is written.
/// </remarks>
internal sealed class Vp3Encoder {
  private const int _QUANTISATION_INDEX = 63;

  /// <summary>How far a motion search looks, in whole samples, around the zero vector.</summary>
  /// <remarks>
  /// The literal component form states a magnitude of at most thirty-one in half-sample steps, which
  /// is fifteen and a half whole samples. Fifteen keeps every candidate inside that.
  /// </remarks>
  private const int _SEARCH_RANGE = 15;

  private readonly int _width;
  private readonly int _height;
  private readonly Vp3Geometry _geometry;
  private readonly short[] _coefficients;
  private readonly int[][] _quantisers = [new int[64], new int[64], new int[64]];
  private readonly int[] _spatial = new int[64];
  private readonly double[] _transformed = new double[64];

  private readonly bool[] _coded;
  private readonly byte[] _modes;
  private readonly int[] _vectorX;
  private readonly int[] _vectorY;
  private readonly bool[] _partialScratch;
  private readonly bool[] _wholeScratch;
  private readonly bool[] _insideScratch;
  private readonly int[] _predictor = new int[64];

  /// <summary>Driven on this encoder's own output, to hand back the frame an inter frame predicts from.</summary>
  private readonly Vp3Decoder _reconstruction;

  private Vp3Frame? _reference;

  internal Vp3Encoder(int width, int height) {
    if (width <= 0 || height <= 0)
      throw new NotSupportedException($"VP3 needs a positive picture size; {width}x{height} was supplied.");

    this._width = width;
    this._height = height;
    this._geometry = new((width + 15) / 16, (height + 15) / 16);
    this._coefficients = new short[this._geometry.BlockCount * 64];

    for (var plane = 0; plane < 3; ++plane)
      Vp3Quantisation.Build(0, plane, _QUANTISATION_INDEX, this._quantisers[plane]);

    this._coded = new bool[this._geometry.BlockCount];
    this._modes = new byte[this._geometry.MacroblockCount];
    this._vectorX = new int[this._geometry.MacroblockCount];
    this._vectorY = new int[this._geometry.MacroblockCount];
    this._partialScratch = new bool[this._geometry.SuperBlockCount];
    this._wholeScratch = new bool[this._geometry.SuperBlockCount];
    this._insideScratch = new bool[this._geometry.BlockCount];
    this._reconstruction = new(width, height);
  }

  internal byte[] Encode(RawImage frame, bool keyFrame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"The VP3 encoder was created for {this._width}x{this._height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var source = FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8, RawImageColorInfo.Bt601Limited);
    var bytes = keyFrame || this._reference == null ? this._Intra(source) : this._Inter(source);

    // Reconstruct by decoding what was just written, so the next inter frame predicts from what its
    // decoder will hold.
    this._reference = this._reconstruction.Decode(bytes);
    return bytes;
  }

  private byte[] _Intra(RawImage source) {
    Array.Clear(this._coefficients);
    for (var block = 0; block < this._geometry.BlockCount; ++block)
      this._TransformBlock(source, block);

    Vp3DcPrediction.ApplyIntra(this._geometry, this._coefficients);
    Vp3BlockFlags.All(this._coded, this._geometry.BlockCount);
    Vp3ModeReader.AllIntra(this._modes, this._geometry.MacroblockCount);

    var writer = new Vp3BitWriter();
    Vp3FrameWriter.WriteIntraHeader(writer, _QUANTISATION_INDEX);
    Vp3TokenWriter.Write(writer, this._geometry, this._coefficients, coded: null);
    return writer.ToArray();
  }

  private byte[] _Inter(RawImage source) {
    var reference = this._reference!;
    Array.Clear(this._coefficients);
    Array.Clear(this._coded);

    for (var macroblock = 0; macroblock < this._geometry.MacroblockCount; ++macroblock) {
      var (x, y) = this._SearchMotion(source, reference, macroblock);
      this._vectorX[macroblock] = x;
      this._vectorY[macroblock] = y;
      this._modes[macroblock] = (byte)(x == 0 && y == 0
        ? Vp3ModeWriter.INTER_NO_MOTION
        : Vp3ModeWriter.INTER_MOTION);

      foreach (var block in this._geometry.MacroblockLumaBlocks[macroblock])
        this._coded[block] = this._TransformResidual(source, reference, block, x, y);

      foreach (var block in this._geometry.MacroblockChromaBlocks[macroblock])
        this._coded[block] = this._TransformResidual(source, reference, block, x, y);
    }

    this._RaiseCodedToSuperBlocks();

    // A macro block whose blocks all fell away carries no mode, and the reader takes its silence as
    // "inter, no motion". The encoder's own arrays have to agree with that, because the DC predictor
    // asks every block which reference its macro block used.
    for (var macroblock = 0; macroblock < this._geometry.MacroblockCount; ++macroblock)
      if (!Vp3ModeWriter._CarriesAMode(this._geometry, this._coded, macroblock))
        this._modes[macroblock] = Vp3ModeWriter.INTER_NO_MOTION;

    Vp3DcPrediction.Apply(this._geometry, this._coded, this._modes, this._coefficients);

    var writer = new Vp3BitWriter();
    Vp3FrameWriter.WriteInterHeader(writer, _QUANTISATION_INDEX);
    Vp3BlockFlagWriter.Write(
      writer, this._geometry, this._coded, this._partialScratch, this._wholeScratch, this._insideScratch);
    Vp3ModeWriter.WriteModes(writer, this._geometry, this._coded, this._modes);
    Vp3ModeWriter.WriteMotionVectors(
      writer, this._geometry, this._coded, this._modes, this._vectorX, this._vectorY);
    Vp3TokenWriter.Write(writer, this._geometry, this._coefficients, this._coded);
    return writer.ToArray();
  }

  /// <summary>
  /// Promotes every block of a super block that has any coded block in it, so no super block is ever
  /// partly coded.
  /// </summary>
  /// <remarks>
  /// A block promoted this way carries no coefficients, and costs one end-of-block token to say so.
  /// That is the price of leaving the block-level run-coded pass empty; see this type's remarks.
  /// </remarks>
  private void _RaiseCodedToSuperBlocks() {
    Array.Clear(this._wholeScratch);
    for (var block = 0; block < this._geometry.BlockCount; ++block)
      if (this._coded[block])
        this._wholeScratch[this._geometry.BlockSuperBlock[block]] = true;

    for (var block = 0; block < this._geometry.BlockCount; ++block)
      this._coded[block] = this._wholeScratch[this._geometry.BlockSuperBlock[block]];
  }

  /// <summary>
  /// Finds the whole-sample vector whose luminance prediction differs least from the source, in the
  /// half-sample units a vector is stated in.
  /// </summary>
  /// <remarks>
  /// The search is whole-sample, so the vector it returns is always even and its luminance prediction
  /// is a plain copy rather than the average of two rows or columns. The zero vector is the incumbent
  /// and only a strictly better candidate displaces it: a macro block that did not move has to come
  /// out of here with a zero vector, or it is coded as motion that did not happen and its super block
  /// is raised along with it.
  /// </remarks>
  private (int X, int Y) _SearchMotion(RawImage source, Vp3Frame reference, int macroblock) {
    var luma = this._geometry.MacroblockLumaBlocks[macroblock];
    var originX = this._geometry.BlockColumn[luma[0]] * 8;
    var originY = this._geometry.BlockRow[luma[0]] * 8;

    var best = (X: 0, Y: 0);
    var bestCost = this._MatchCost(source, reference, originX, originY, 0, 0, int.MaxValue);

    for (var candidateY = -_SEARCH_RANGE; candidateY <= _SEARCH_RANGE; ++candidateY)
    for (var candidateX = -_SEARCH_RANGE; candidateX <= _SEARCH_RANGE; ++candidateX) {
      var cost = this._MatchCost(source, reference, originX, originY, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (2 * candidateX, 2 * candidateY);
    }

    return best;
  }

  private int _MatchCost(
    RawImage source, Vp3Frame reference, int originX, int originY, int vectorX, int vectorY, int ceiling) {
    var (sourceWidth, sourceHeight) = source.GetPlaneDimensions(0);
    var samples = source.GetPlaneData(0);
    var plane = reference.Luma;
    var planeWidth = reference.LumaWidth;
    var planeHeight = reference.LumaHeight;

    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y) {
      // VP3's planes run bottom-up and the source image runs top-down, so the row a block's origin
      // names is counted from the other end of the picture.
      var sourceRow = Math.Max(0, sourceHeight - 1 - (originY + y)) * sourceWidth;
      var referenceRow = _Clamp(originY + y + vectorY, planeHeight) * planeWidth;
      for (var x = 0; x < 16; ++x)
        cost += Math.Abs(
          samples[sourceRow + Math.Min(sourceWidth - 1, originX + x)]
          - plane[referenceRow + _Clamp(originX + x + vectorX, planeWidth)]);
    }

    return cost;
  }

  private static int _Clamp(int value, int limit) => value < 0 ? 0 : value >= limit ? limit - 1 : value;

  /// <summary>
  /// Transforms and quantises one block of source-minus-prediction, and answers whether anything
  /// survived.
  /// </summary>
  private bool _TransformResidual(RawImage source, Vp3Frame reference, int block, int vectorX, int vectorY) {
    var plane = this._geometry.BlockPlane[block];
    var (sourceWidth, sourceHeight) = source.GetPlaneDimensions(plane);
    var samples = source.GetPlaneData(plane);
    var originX = this._geometry.BlockColumn[block] * 8;
    var originY = this._geometry.BlockRow[block] * 8;

    // Two vector steps make a luminance sample and four make a chrominance one, which is how the same
    // stated vector serves both planes of a 4:2:0 macro block.
    Vp3Prediction.Inter(
      reference.Plane(plane), plane == 0 ? reference.LumaWidth : reference.ChromaWidth,
      plane == 0 ? reference.LumaHeight : reference.ChromaHeight,
      originX, originY, vectorX, vectorY, plane == 0 ? 2 : 4, this._predictor);

    for (var y = 0; y < 8; ++y) {
      var sourceY = Math.Max(0, sourceHeight - 1 - (originY + y));
      for (var x = 0; x < 8; ++x) {
        var sourceX = Math.Min(sourceWidth - 1, originX + x);
        this._spatial[y * 8 + x] = samples[sourceY * sourceWidth + sourceX] - this._predictor[y * 8 + x];
      }
    }

    Vp3ForwardDct.Transform(this._spatial, this._transformed);

    var quantiser = this._quantisers[plane];
    var at = block * 64;
    var coded = false;
    for (var natural = 0; natural < 64; ++natural) {
      var quantised = (int)Math.Round(this._transformed[natural] / quantiser[natural], MidpointRounding.AwayFromZero);
      if (quantised is < -580 or > 580)
        throw new InvalidOperationException(
          $"VP3 quantisation produced coefficient {quantised} at natural position {natural}; the format's largest scalar token is 580.");

      this._coefficients[at + Vp3Tables.ZigZag[natural]] = (short)quantised;
      coded |= quantised != 0;
    }

    return coded;
  }

  private void _TransformBlock(RawImage source, int block) {
    var plane = this._geometry.BlockPlane[block];
    var (sourceWidth, sourceHeight) = source.GetPlaneDimensions(plane);
    var samples = source.GetPlaneData(plane);
    var originX = this._geometry.BlockColumn[block] * 8;
    var originY = this._geometry.BlockRow[block] * 8;

    for (var y = 0; y < 8; ++y) {
      var sourceY = Math.Max(0, sourceHeight - 1 - (originY + y));
      for (var x = 0; x < 8; ++x) {
        var sourceX = Math.Min(sourceWidth - 1, originX + x);
        this._spatial[y * 8 + x] = samples[sourceY * sourceWidth + sourceX] - 128;
      }
    }

    Vp3ForwardDct.Transform(this._spatial, this._transformed);

    var quantiser = this._quantisers[plane];
    var at = block * 64;
    for (var natural = 0; natural < 64; ++natural) {
      var quantised = (int)Math.Round(this._transformed[natural] / quantiser[natural], MidpointRounding.AwayFromZero);
      if (quantised is < -580 or > 580)
        throw new InvalidOperationException(
          $"VP3 quantisation produced coefficient {quantised} at natural position {natural}; the format's largest scalar token is 580.");

      this._coefficients[at + Vp3Tables.ZigZag[natural]] = (short)quantised;
    }
  }
}
