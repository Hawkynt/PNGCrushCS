namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// How many chrominance samples a picture carries for each luminance one
/// (ISO/IEC 13818-2, 6.3.5, <c>chroma_format</c>).
/// </summary>
/// <remarks>
/// MPEG-1 has no such field and is always <see cref="Yuv420"/>; the value only becomes a choice in
/// MPEG-2, where it decides three separate things at once — how large the chrominance planes are, how
/// many blocks a macroblock holds, and by how much a motion vector is scaled before it is applied to
/// chrominance. Getting any one of those wrong produces a picture whose luminance is perfect, which
/// is why the format is carried as a value rather than assumed anywhere.
/// </remarks>
internal enum MpegChromaFormat {

  /// <summary>Half the luminance resolution in both directions. The only format MPEG-1 has.</summary>
  Yuv420 = 1,

  /// <summary>Half horizontally, full vertically.</summary>
  Yuv422 = 2,

  /// <summary>A chrominance sample for every luminance one.</summary>
  Yuv444 = 3,
}
