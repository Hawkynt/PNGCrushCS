using System;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Encodes an H.263+ I- or P-picture whose header uses PLUSPTYPE, reusing the baseline macroblock layer.
/// </summary>
internal sealed class H263PlusPictureEncoder {

  private readonly int _width;
  private readonly int _height;
  private readonly int _sourceFormat;
  private readonly int _temporalReference;
  private readonly int _quantiser;
  private readonly H263Frame _source;
  private readonly H263Frame? _reference;

  internal H263PlusPictureEncoder(
    int width,
    int height,
    int sourceFormat,
    int temporalReference,
    int quantiser,
    H263Frame source,
    H263Frame? reference = null) {
    this._width = width;
    this._height = height;
    this._sourceFormat = sourceFormat;
    this._temporalReference = temporalReference;
    this._quantiser = quantiser;
    this._source = source ?? throw new ArgumentNullException(nameof(source));
    this._reference = reference;
  }

  internal byte[] Encode() {
    var writer = new H263BitWriter();
    H263PlusHeaderWriter.Write(
      writer,
      this._width,
      this._height,
      this._sourceFormat,
      this._temporalReference,
      this._quantiser,
      this._reference == null ? H263PictureKind.Intra : H263PictureKind.Predicted);

    H263PictureEncoder.ForMacroblockLayer(this._source, this._quantiser, this._reference, writer).EncodeMacroblocks();
    return writer.ToArray();
  }
}
