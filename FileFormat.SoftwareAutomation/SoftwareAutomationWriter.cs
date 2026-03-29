using System;

namespace FileFormat.SoftwareAutomation;

/// <summary>Assembles Software Automation Graphics bytes from a <see cref="SoftwareAutomationFile"/>.</summary>
public static class SoftwareAutomationWriter {

  public static byte[] ToBytes(SoftwareAutomationFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SoftwareAutomationFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, SoftwareAutomationFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
