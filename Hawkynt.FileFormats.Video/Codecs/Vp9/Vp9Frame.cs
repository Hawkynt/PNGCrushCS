namespace FileFormat.Codecs.Vp9;

/// <summary>
/// One decoded VP9 picture. Samples are held in sixteen-bit containers so the same reconstruction
/// path represents profiles 0/1 (8-bit) and profiles 2/3 (10/12-bit) without narrowing references.
/// </summary>
internal sealed class Vp9Frame {

  internal readonly int Width;
  internal readonly int Height;
  internal readonly int BitDepth;
  internal readonly int SubsamplingX;
  internal readonly int SubsamplingY;
  internal readonly int ColorSpace;
  internal readonly int ColorRange;

  internal readonly int LumaWidth;
  internal readonly int LumaHeight;
  internal readonly int ChromaWidth;
  internal readonly int ChromaHeight;

  internal readonly ushort[] Luma;
  internal readonly ushort[] Cb;
  internal readonly ushort[] Cr;

  internal Vp9Frame(
    int width, int height, int superblockColumns, int superblockRows,
    int bitDepth, int subsamplingX, int subsamplingY, int colorSpace, int colorRange) {
    this.Width = width;
    this.Height = height;
    this.BitDepth = bitDepth;
    this.SubsamplingX = subsamplingX;
    this.SubsamplingY = subsamplingY;
    this.ColorSpace = colorSpace;
    this.ColorRange = colorRange;

    this.LumaWidth = superblockColumns * 64;
    this.LumaHeight = superblockRows * 64;
    this.ChromaWidth = this.LumaWidth >> subsamplingX;
    this.ChromaHeight = this.LumaHeight >> subsamplingY;

    this.Luma = new ushort[this.LumaWidth * this.LumaHeight];
    this.Cb = new ushort[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new ushort[this.ChromaWidth * this.ChromaHeight];
  }

  internal ushort[] Plane(int plane) => plane switch {
    0 => this.Luma,
    1 => this.Cb,
    _ => this.Cr,
  };

  internal int Stride(int plane) => plane == 0 ? this.LumaWidth : this.ChromaWidth;

  internal int PlaneHeight(int plane) => plane == 0 ? this.LumaHeight : this.ChromaHeight;

  internal int LastColumn(int plane)
    => plane == 0 ? this.Width - 1 : ((this.Width + (1 << this.SubsamplingX) - 1) >> this.SubsamplingX) - 1;

  internal int LastRow(int plane)
    => plane == 0 ? this.Height - 1 : ((this.Height + (1 << this.SubsamplingY) - 1) >> this.SubsamplingY) - 1;

  internal bool Matches(
    int width, int height, int superblockColumns, int superblockRows,
    int bitDepth, int subsamplingX, int subsamplingY, int colorSpace, int colorRange)
    => this.Width == width && this.Height == height
       && this.BitDepth == bitDepth
       && this.SubsamplingX == subsamplingX && this.SubsamplingY == subsamplingY
       && this.ColorSpace == colorSpace && this.ColorRange == colorRange
       && this.LumaWidth == superblockColumns * 64 && this.LumaHeight == superblockRows * 64;
}
