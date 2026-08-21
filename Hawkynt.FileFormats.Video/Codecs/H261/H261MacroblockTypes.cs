namespace FileFormat.Codecs.H261;

/// <summary>The four kinds of prediction Table 2/H.261 distinguishes.</summary>
internal enum H261PredictionKind {

  /// <summary>Coded on its own; every one of its six blocks always carries coefficients.</summary>
  Intra,

  /// <summary>Predicted from the co-located block of the reference, no motion, no filter.</summary>
  Inter,

  /// <summary>Predicted with a coded motion vector, no filter.</summary>
  InterWithMotionCompensation,

  /// <summary>Predicted with a coded motion vector, and the loop filter of clause 3.2.3 applied first.</summary>
  InterWithMotionCompensationAndFilter,
}

/// <summary>One row of Table 2/H.261: which fields a macroblock of this type carries.</summary>
/// <param name="Kind">What the macroblock predicts from.</param>
/// <param name="HasQuantiser">Whether MQUANT follows MTYPE.</param>
/// <param name="HasMotionVector">Whether MVD follows.</param>
/// <param name="HasCodedBlockPattern">Whether CBP follows.</param>
internal readonly record struct H261MacroblockType(
  H261PredictionKind Kind, bool HasQuantiser, bool HasMotionVector, bool HasCodedBlockPattern) {

  /// <summary>
  /// Whether every one of the macroblock's six blocks carries coefficients regardless of any coded
  /// block pattern — true for Intra, which Table 2 gives no CBP field at all.
  /// </summary>
  internal bool AllBlocksCoded => this.Kind == H261PredictionKind.Intra;

  /// <summary>
  /// Table 2/H.261, in the order <see cref="H261VlcTables.MacroblockType"/> assigns its values.
  /// </summary>
  internal static readonly H261MacroblockType[] All = [
    new(H261PredictionKind.Intra, HasQuantiser: false, HasMotionVector: false, HasCodedBlockPattern: false),
    new(H261PredictionKind.Intra, HasQuantiser: true, HasMotionVector: false, HasCodedBlockPattern: false),
    new(H261PredictionKind.Inter, HasQuantiser: false, HasMotionVector: false, HasCodedBlockPattern: true),
    new(H261PredictionKind.Inter, HasQuantiser: true, HasMotionVector: false, HasCodedBlockPattern: true),
    new(H261PredictionKind.InterWithMotionCompensation,
      HasQuantiser: false, HasMotionVector: true, HasCodedBlockPattern: false),
    new(H261PredictionKind.InterWithMotionCompensation,
      HasQuantiser: false, HasMotionVector: true, HasCodedBlockPattern: true),
    new(H261PredictionKind.InterWithMotionCompensation,
      HasQuantiser: true, HasMotionVector: true, HasCodedBlockPattern: true),
    new(H261PredictionKind.InterWithMotionCompensationAndFilter,
      HasQuantiser: false, HasMotionVector: true, HasCodedBlockPattern: false),
    new(H261PredictionKind.InterWithMotionCompensationAndFilter,
      HasQuantiser: false, HasMotionVector: true, HasCodedBlockPattern: true),
    new(H261PredictionKind.InterWithMotionCompensationAndFilter,
      HasQuantiser: true, HasMotionVector: true, HasCodedBlockPattern: true),
  ];
}
