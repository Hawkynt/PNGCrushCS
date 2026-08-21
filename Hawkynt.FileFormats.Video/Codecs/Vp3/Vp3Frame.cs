using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// One decoded picture: three 8-bit planes at 4:2:0, sized to whole macro blocks.
/// </summary>
/// <remarks>
/// <b>Row zero is the bottom of the picture.</b> VP3 and Theora are written in a right-handed
/// coordinate system with the origin in the lower-left corner, and every rule in the format that says
/// "below" or "the lower-left corner of the block" means it in that system. Storing the planes the
/// same way up means those rules can be written down as they are stated rather than mirrored at each
/// use, and mirroring them is exactly the kind of change that is right in fifteen places and wrong in
/// the sixteenth. The picture is turned the right way up once, on the way out, by
/// <see cref="Vp3ColorConversion"/>.
/// <para/>
/// The planes are a whole number of macro blocks across and down. VP3 codes whole macro blocks, and
/// where the container states a size that is not a multiple of sixteen the samples past the edge are
/// still coded samples that later frames predict from, so they are kept and the crop happens on the
/// way out.
/// </remarks>
internal sealed class Vp3Frame {

  internal readonly int LumaWidth;
  internal readonly int LumaHeight;
  internal readonly int ChromaWidth;
  internal readonly int ChromaHeight;

  internal readonly byte[] Luma;
  internal readonly byte[] Cb;
  internal readonly byte[] Cr;

  internal Vp3Frame(int macroblockColumns, int macroblockRows) {
    this.LumaWidth = macroblockColumns * 16;
    this.LumaHeight = macroblockRows * 16;
    this.ChromaWidth = macroblockColumns * 8;
    this.ChromaHeight = macroblockRows * 8;

    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  /// <summary>One of the three planes by its index, as Table 2.1 numbers them.</summary>
  internal byte[] Plane(int index) => index switch {
    0 => this.Luma,
    1 => this.Cb,
    _ => this.Cr,
  };

  /// <summary>
  /// One plane, cropped to a picture size and turned the right way up.
  /// </summary>
  /// <remarks>
  /// Where the picture sits inside the coded frame only matters when the container states a size that
  /// is not a whole number of macro blocks, and then it matters a great deal: a frame cropped from the
  /// wrong end is wrong in every sample. It sits at the <b>upper</b> left, so the rows that belong to
  /// it are the last <paramref name="height"/> of the plane counting from the bottom, and the coded
  /// padding is the rows underneath and the columns to the right.
  /// <para/>
  /// That is not what Section 6.2 of the Theora specification suggests, which says a Theora header
  /// written for VP3 content should place the picture at the lower left with both offsets zero. But
  /// the same paragraph says VP3 "does not correctly handle frame sizes that are not a multiple of
  /// sixteen", and this is that case, so the specification is describing what a transcoder should
  /// write rather than what VP3 files contain. Two of the streams this was tested against state such a
  /// size — 280&#215;200 coded as 288&#215;208, and 350&#215;141 coded as 352&#215;144 — and against a
  /// reference decoder the upper-left crop matches every sample of every frame while the lower-left
  /// crop matches almost none. Every offset was tried; only this one agrees.
  /// </remarks>
  internal byte[] TopDown(int index, int width, int height) {
    var source = this.Plane(index);
    var stride = index == 0 ? this.LumaWidth : this.ChromaWidth;
    var rows = index == 0 ? this.LumaHeight : this.ChromaHeight;
    var destination = new byte[width * height];

    for (var y = 0; y < height; ++y)
      Array.Copy(source, (rows - 1 - y) * stride, destination, y * width, width);

    return destination;
  }

  /// <summary>Copies every plane of another frame over this one.</summary>
  internal void CopyFrom(Vp3Frame other) {
    other.Luma.CopyTo(this.Luma, 0);
    other.Cb.CopyTo(this.Cb, 0);
    other.Cr.CopyTo(this.Cr, 0);
  }
}
