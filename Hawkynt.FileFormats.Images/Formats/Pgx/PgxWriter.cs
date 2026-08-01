using System;
using System.Text;

namespace FileFormat.Pgx;

/// <summary>Assembles a PGX image from a <see cref="PgxFile"/>.</summary>
public static class PgxWriter {

  public static byte[] ToBytes(PgxFile file) {
    var samples = file.Samples ?? [];
    var header = Encoding.ASCII.GetBytes(
      $"PG {(file.IsBigEndian ? "ML" : "LM")} {(file.IsSigned ? "-" : "+")} {file.Depth} {file.Width} {file.Height}\n");

    var count = file.Width * file.Height;
    var result = new byte[header.Length + count];
    header.CopyTo(result, 0);
    samples.AsSpan(0, Math.Min(samples.Length, count)).CopyTo(result.AsSpan(header.Length));

    return result;
  }
}
