namespace FileFormat.Pax;

/// <summary>The cipher a Pick Ax file's pixel rows are encrypted with.</summary>
/// <remarks>
/// The number is stored in the header's version word, so it is only known once the header has been
/// decrypted. The header, palette and tags are always plain Blowfish whatever is chosen here.
/// </remarks>
public enum PaxEncryption {
  /// <summary>Blowfish, each eight-byte block on its own.</summary>
  Blowfish = 1,
  /// <summary>Blowfish chained block to block, restarted for every row header and every row.</summary>
  BlowfishCbc = 2,
  /// <summary>Sapphire II, rekeyed for every row header and every row.</summary>
  Sapphire = 3,
  /// <summary>Blowfish, then Sapphire II over the result.</summary>
  BlowfishThenSapphire = 4,
  /// <summary>Sapphire II, then Blowfish over the result.</summary>
  SapphireThenBlowfish = 5,
  /// <summary>Chained Blowfish, then Sapphire II over the result.</summary>
  BlowfishCbcThenSapphire = 6,
}
