namespace FileFormat.Codecs.Vp9;

/// <summary>
/// One decoded picture: three eight-bit planes at 4:2:0, sized to whole superblocks.
/// </summary>
/// <remarks>
/// The planes are a whole number of 64x64 superblocks across and down rather than the size the frame
/// header states, and superblocks rather than the eight-sample blocks the picture size rounds up to.
/// A 64x64 prediction block is allowed as long as its middle is on screen, so a block can reach as
/// much as twenty-four samples past the last on-screen row, and prediction writes all of it. Anything
/// smaller than a superblock would be a buffer with an edge in the middle of a legal block. The crop
/// happens once, on the way out.
/// <para/>
/// What the crop is <em>not</em> is a limit on what a later frame may predict from. Inter prediction
/// clamps its reads to the stated picture size, so the samples beyond it are written and then never
/// looked at again — which is the same answer a decoder that cropped and replicated would give, for
/// the cost of not having to copy anything.
/// <para/>
/// Each picture remembers its own size because VP9 lets it differ from frame to frame. A frame may
/// predict from a reference half its width or sixteen times smaller, and the scaling that needs is
/// computed from the two frames' sizes — so a buffer that did not know its own would have to be told,
/// by whoever happened to be holding it.
/// </remarks>
internal sealed class Vp9Frame {

  /// <summary>The picture size the frame header stated, which is what is shown and what is predicted from.</summary>
  internal readonly int Width;

  internal readonly int Height;

  internal readonly int LumaWidth;
  internal readonly int LumaHeight;
  internal readonly int ChromaWidth;
  internal readonly int ChromaHeight;

  internal readonly byte[] Luma;
  internal readonly byte[] Cb;
  internal readonly byte[] Cr;

  internal Vp9Frame(int width, int height, int superblockColumns, int superblockRows) {
    this.Width = width;
    this.Height = height;

    this.LumaWidth = superblockColumns * 64;
    this.LumaHeight = superblockRows * 64;
    this.ChromaWidth = this.LumaWidth >> 1;
    this.ChromaHeight = this.LumaHeight >> 1;

    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  internal byte[] Plane(int plane) => plane switch {
    0 => this.Luma,
    1 => this.Cb,
    _ => this.Cr,
  };

  internal int Stride(int plane) => plane == 0 ? this.LumaWidth : this.ChromaWidth;

  internal int PlaneHeight(int plane) => plane == 0 ? this.LumaHeight : this.ChromaHeight;

  /// <summary>The last column of this picture's visible area in a plane, which is where reads are clamped.</summary>
  internal int LastColumn(int plane) => plane == 0 ? this.Width - 1 : ((this.Width + 1) >> 1) - 1;

  internal int LastRow(int plane) => plane == 0 ? this.Height - 1 : ((this.Height + 1) >> 1) - 1;

  internal bool Matches(int width, int height, int superblockColumns, int superblockRows)
    => this.Width == width && this.Height == height
       && this.LumaWidth == superblockColumns * 64 && this.LumaHeight == superblockRows * 64;
}
