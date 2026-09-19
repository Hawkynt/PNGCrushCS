namespace FileFormat.Codecs.H263;

/// <summary>The picture type carried by baseline PTYPE or the mandatory part of PLUSPTYPE.</summary>
internal enum H263PictureKind : byte {
  Intra = 0,
  Predicted = 1,
  ImprovedPb = 2,
  Bidirectional = 3,
  EnhancementIntra = 4,
  EnhancementPredicted = 5,
}
