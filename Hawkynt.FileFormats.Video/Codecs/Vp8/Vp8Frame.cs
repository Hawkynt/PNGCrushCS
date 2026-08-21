namespace FileFormat.Codecs.Vp8;

/// <summary>
/// One decoded picture: three 8-bit planes at 4:2:0, sized to whole macroblocks.
/// </summary>
/// <remarks>
/// The planes are a whole number of macroblocks across and down, not the size the frame header
/// states. VP8 codes whole macroblocks whatever the picture size, and the samples past the right and
/// bottom edges are real coded samples that later frames predict from — so a decoder that cropped
/// them away would have to invent them again the moment an inter frame pointed at them. The crop
/// happens once, on the way out.
/// <para/>
/// There is no border of replicated edge samples around the planes, which is how most decoders make
/// motion vectors that point outside the picture cheap to follow.
/// <see cref="Vp8InterPrediction"/> clamps the coordinates it reads instead. That gives the same
/// samples a border would — a border is edge replication, written down in advance — for any distance
/// rather than for the finite one a border covers, and a motion vector is allowed to point thousands
/// of pixels outside the frame.
/// </remarks>
internal sealed class Vp8Frame {

  internal readonly int LumaWidth;
  internal readonly int LumaHeight;
  internal readonly int ChromaWidth;
  internal readonly int ChromaHeight;

  internal readonly byte[] Luma;
  internal readonly byte[] Cb;
  internal readonly byte[] Cr;

  internal Vp8Frame(int macroblockColumns, int macroblockRows) {
    this.LumaWidth = macroblockColumns * 16;
    this.LumaHeight = macroblockRows * 16;
    this.ChromaWidth = macroblockColumns * 8;
    this.ChromaHeight = macroblockRows * 8;

    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }
}
