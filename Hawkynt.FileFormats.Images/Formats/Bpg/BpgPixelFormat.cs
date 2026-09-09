namespace FileFormat.Bpg;

/// <summary>
/// The chroma subsampling a BPG picture uses, as the three-bit <c>pixel_format</c> field spells it.
/// </summary>
/// <remarks>
/// These are the numbers written into the file, so the values matter as much as the names: a
/// decoder derives <c>chroma_format_idc</c> straight from this field. Two of the six say the same
/// subsampling as another two and differ only in where the chroma sample sits relative to the luma
/// grid — JPEG puts it in the middle of the four luma samples it covers, MPEG-2 puts it on the left
/// edge — which changes the picture an upsampler reconstructs, not its shape.
/// </remarks>
public enum BpgPixelFormat {

  /// <summary>One luma plane and nothing else. <c>color_space</c> must be zero alongside it.</summary>
  Grayscale = 0,

  /// <summary>4:2:0, chroma at (0.5, 0.5) — the position JPEG uses.</summary>
  YCbCr420 = 1,

  /// <summary>4:2:2, chroma at (0.5, 0) — the position JPEG uses.</summary>
  YCbCr422 = 2,

  /// <summary>4:4:4: a full-resolution plane per component.</summary>
  YCbCr444 = 3,

  /// <summary>4:2:0, chroma at (0, 0.5) — the position MPEG-2 uses.</summary>
  YCbCr420Mpeg2 = 4,

  /// <summary>4:2:2, chroma at (0, 0) — the position MPEG-2 uses.</summary>
  YCbCr422Mpeg2 = 5,
}
