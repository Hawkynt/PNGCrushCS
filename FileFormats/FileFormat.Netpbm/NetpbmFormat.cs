namespace FileFormat.Netpbm;

/// <summary>The Netpbm sub-format identified by the magic number (P1-P7).</summary>
public enum NetpbmFormat {
  PbmAscii = 1,
  PgmAscii = 2,
  PpmAscii = 3,
  PbmBinary = 4,
  PgmBinary = 5,
  PpmBinary = 6,
  Pam = 7
}
