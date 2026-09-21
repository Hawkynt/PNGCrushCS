namespace FileFormat.Codecs.HuffYuv;

/// <summary>A code book capable of reading one HuffYUV residual symbol.</summary>
internal interface IHuffYuvDecoderTable {
  int Read(HuffYuvBitReader bits);
}
