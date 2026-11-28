namespace FileFormat.LuraWave;

/// <summary>
/// The two check values LuraTech derives from an access password: a 16-bit one in the <c>PW</c> chunk, against which
/// a password is tried first, and a 32-bit one embedded in the coded data where the public quality ends.
/// </summary>
/// <remarks>
/// Both hash at most the first 32 characters. Letters, digits and <c>+-/*!"$%&amp;()=?_</c> and space count with their
/// own code; every other character counts as a fixed value. Recovered from LuraWave SmartCompress 3.0, whose encoder
/// writes both values and whose decoder checks both.
/// </remarks>
internal static class LuraWavePassword {

  private const int MaxLength = 32;

  private static bool Counts(char c)
    => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
      or '+' or '-' or '/' or '*' or '!' or '"' or '$' or '%' or '&' or '(' or ')' or '=' or '?' or '_' or ' ';

  /// <summary>The 16-bit value stored in the <c>PW</c> chunk; never 0.</summary>
  public static ushort HeaderCheck(string password) {
    ushort check = 0;
    for (var i = 0; i < password.Length && i < MaxLength && password[i] != '\0'; ++i) {
      var c = password[i];
      check = Counts(c) ? (ushort)(check * 5 + c / 3) : (ushort)((check + 13) * 5);
    }

    return check == 0 ? (ushort)0x1645 : check;
  }

  /// <summary>The 32-bit value embedded in the coded data; never 0.</summary>
  public static uint StreamCheck(string password) {
    uint check = 0;
    for (var i = 0; i < password.Length && i < MaxLength && password[i] != '\0'; ++i) {
      var c = password[i];
      check = check * 33 + (Counts(c) ? c : 0x4Du);
    }

    return check == 0 ? 0x12674451u : check;
  }
}
