using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.RealVideo;

/// <summary>Encodes one RealVideo 1 picture, intra or predicted, over the shared H.263 macroblock layer.</summary>
internal sealed class RealVideoPictureEncoder {

  private readonly H263Frame _source;
  private readonly H263Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _quantiser;

  internal RealVideoPictureEncoder(H263Frame source, int quantiser, H263Frame? reference = null) {
    this._source = source;
    this._reference = reference;
    this._macroblockWidth = source.LumaWidth / 16;
    this._macroblockHeight = source.LumaHeight / 16;
    this._quantiser = quantiser;
  }

  internal byte[] Encode() {
    var macroblockCount = this._macroblockWidth * this._macroblockHeight;
    var writer = new H263BitWriter();

    // RealVideo 1 picture header: marker, picture type, the PB-frame bit, quantiser, the explicit run
    // position, run length and three reserved bits. One run covers the whole picture. The reference
    // encoder writes the same shape, and the position fields are stated even to say nought -- a
    // header that leaves them out means the whole picture by it, and the decoder beside this refuses
    // that form rather than guessing where the bits after it sit.
    writer.WriteBit(1);
    writer.WriteBit(this._reference == null ? 0 : 1);
    writer.WriteBit(0);
    writer.Write(this._quantiser, 5);
    writer.Write(0, 6);
    writer.Write(0, 6);
    writer.Write(macroblockCount, 12);
    writer.Write(0, 3);

    // The macroblock layer is H.263's, written by H.263's own encoder. The decoder beside this hands
    // the same layer to H263PictureDecoder, so the two directions share one statement of the median
    // vector predictor, the complemented CBPY of an inter macroblock and the COD rule.
    H263PictureEncoder.ForMacroblockLayer(this._source, this._quantiser, this._reference, writer)
      .EncodeMacroblocks();

    return writer.ToArray();
  }
}
