namespace FileFormat.Codecs.Asv;

/// <summary>Which of the two ASUS codecs a picture is being written as.</summary>
/// <remarks>
/// The two share their macroblock shape, their scan, their quantisation matrix and their picture
/// layout; they differ in how a block's coefficients are coded, in how the finished packet is stored,
/// and in the scale their dequantisation uses. That is small enough to be one encoder told which one
/// it is rather than two encoders that would drift apart.
/// </remarks>
internal enum AsvVersion {

  /// <summary>ASUS V1: End-Of-Block-terminated coefficient groups, packets byte-swapped by word.</summary>
  Version1,

  /// <summary>ASUS V2: an explicit coefficient-group count, packets stored with each byte's bits reversed.</summary>
  Version2,
}
