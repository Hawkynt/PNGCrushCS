namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// Which of the three bitstreams Microsoft shipped under the name MPEG-4 a stream is.
/// </summary>
/// <remarks>
/// Not three revisions of one format but three formats that share a picture header and a block layer.
/// Nothing in a stream says which it is — there is no start code except version 1's and no version
/// field anywhere — so the four-character code the container states is the only thing that can decide,
/// and a stream decoded as the wrong one produces a picture rather than an error.
/// </remarks>
internal enum MsMpeg4Version {

  /// <summary>
  /// <c>MPG4</c>, <c>MP41</c>, <c>DIV1</c>: the first, which is the closest of the three to H.263.
  /// </summary>
  /// <remarks>
  /// It borrows H.263's macroblock and coded block pattern tables outright, predicts the DC the way
  /// MPEG-1 does rather than from the neighbouring blocks, has no alternating current prediction at
  /// all, reads its slice field as a height rather than as a count, and reaches the block layer's
  /// third escape form directly where the others spend two bits choosing it. It is also the only one
  /// whose picture carries a start code.
  /// </remarks>
  Version1 = 1,

  /// <summary>
  /// <c>MP42</c>, <c>DIV2</c>: the second, and the one that is nearly all standard underneath.
  /// </summary>
  Version2 = 2,

  /// <summary>
  /// <c>MP43</c>, <c>DIV3</c> and the rest: the third, which is the original DivX.
  /// </summary>
  /// <remarks>
  /// The one that carries tables of its own: each picture chooses which of three run-level tables, of
  /// two DC tables and of two motion vector tables it was coded with, states its whole coded block
  /// pattern in one code predicted from the neighbouring blocks, and brings back the varying intra DC
  /// step the other two fix at eight.
  /// </remarks>
  Version3 = 3,
}
