using System.ComponentModel;
using System.Diagnostics;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Starts a tool that is not this project, and skips the test when the machine has not got it.
/// </summary>
/// <remarks>
/// Four tests here asked ImageMagick to check what we had written, and each guarded the call the
/// same wrong way:
/// <code>
/// using var identify = Process.Start(new ProcessStartInfo("identify", ...));
/// if (identify == null)
///   Assert.Ignore("no ImageMagick here to ask");
/// </code>
/// <see cref="Process.Start(ProcessStartInfo)"/> does not return null when the executable is
/// missing. It throws <see cref="Win32Exception"/>, so that guard never ran and the test failed
/// instead of skipping. On this machine ImageMagick is installed and the tests passed, which is why
/// it stood; on a build agent without it every one of the four failed, and the whole suite with
/// them.
/// <para/>
/// Skipping is the right answer rather than failing. These tests assert that something outside this
/// project agrees with us, and a machine with no such tool has not disagreed — it has not been
/// asked. The conformance suite beside this one already treats an absent oracle that way, which is
/// why it passes on the agent while these did not.
/// </remarks>
internal static class ExternalTool {

  /// <summary>Starts a tool, or skips the calling test if it is not installed.</summary>
  /// <param name="fileName">The executable, as it would be typed.</param>
  /// <param name="arguments">Its arguments, already quoted.</param>
  /// <returns>The running process. Never null: the test has been skipped if it would have been.</returns>
  public static Process StartOrIgnore(string fileName, string arguments) {
    try {
      var process = Process.Start(new ProcessStartInfo(fileName, arguments) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      });

      if (process != null)
        return process;
    } catch (Win32Exception) {
      // The executable is not on the path. Which is not a disagreement.
    }

    Assert.Ignore($"no {fileName} on this machine to ask");
    return null!; // Assert.Ignore throws, so this is unreachable.
  }
}
