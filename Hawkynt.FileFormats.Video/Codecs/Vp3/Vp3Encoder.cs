using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Vp3;

/// <summary>Encodes independent VP3.1 intra pictures.</summary>
/// <remarks>
/// The first writer is deliberately conservative: every picture is a key frame, every block uses the
/// intra predictor, and coefficient codebook zero is used throughout. VP3's prediction, quantisation,
/// DC prediction and coefficient syntax are still the real format; only the encoder decisions are
/// simple. That keeps the output interoperable while avoiding a rate-control and motion-search policy
/// masquerading as format support.
/// </remarks>
internal sealed class Vp3Encoder {
  private const int _QUANTISATION_INDEX = 63;

  private readonly int _width;
  private readonly int _height;
  private readonly Vp3Geometry _geometry;
  private readonly short[] _coefficients;
  private readonly int[][] _quantisers = [new int[64], new int[64], new int[64]];
  private readonly int[] _spatial = new int[64];
  private readonly double[] _transformed = new double[64];

  internal Vp3Encoder(int width, int height) {
    if (width <= 0 || height <= 0)
      throw new NotSupportedException($"VP3 needs a positive picture size; {width}x{height} was supplied.");

    this._width = width;
    this._height = height;
    this._geometry = new((width + 15) / 16, (height + 15) / 16);
    this._coefficients = new short[this._geometry.BlockCount * 64];

    for (var plane = 0; plane < 3; ++plane)
      Vp3Quantisation.Build(0, plane, _QUANTISATION_INDEX, this._quantisers[plane]);
  }

  internal byte[] Encode(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"The VP3 encoder was created for {this._width}x{this._height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var source = FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8, RawImageColorInfo.Bt601Limited);

    Array.Clear(this._coefficients);
    for (var block = 0; block < this._geometry.BlockCount; ++block)
      this._TransformBlock(source, block);

    Vp3DcPrediction.ApplyIntra(this._geometry, this._coefficients);

    var writer = new Vp3BitWriter();
    Vp3FrameWriter.WriteIntraHeader(writer, _QUANTISATION_INDEX);
    Vp3TokenWriter.Write(writer, this._geometry, this._coefficients);
    return writer.ToArray();
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
