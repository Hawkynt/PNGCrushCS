using System;

namespace FileFormat.BugbiterApac;

/// <summary>Assembles a Bugbiter APAC239i picture from a <see cref="BugbiterApacFile"/>.</summary>
public static class BugbiterApacWriter {

  /// <summary>
  /// Writes the file, which is already whole because the comment's length is what everything after
  /// the header is addressed from.
  /// </summary>
  public static byte[] ToBytes(BugbiterApacFile file) {
    var source = file.Data ?? [];
    var data = new byte[Math.Max(source.Length, BugbiterApacFile.BaseFileSize)];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
