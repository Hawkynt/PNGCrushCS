namespace FileFormat.ApplePreferred;

/// <summary>Assembles an Apple Preferred Format picture from an <see cref="ApplePreferredFile"/>.</summary>
public static class ApplePreferredWriter {

  /// <summary>Writes the file, which is already whole because every chunk is addressed absolutely.</summary>
  public static byte[] ToBytes(ApplePreferredFile file) => (byte[])(file.Data ?? []).Clone();
}
