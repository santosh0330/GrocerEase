using System.ComponentModel.DataAnnotations;

namespace GrocerEase.Api.Logging;

/// <summary>
/// The <c>LogFiles</c> section: where the log files go and how long they stay. Validated when the app starts, so a typo cannot
/// silently turn off retention (and fill a disk) or write nowhere.
/// </summary>
public sealed class LogFileSettings
{
    public const string SectionName = "LogFiles";

    /// <summary>The file name before the date: <c>grocerease-20261110.log</c>, and <c>grocerease-20261110_001.log</c> once a day's file is full.</summary>
    public const string FileNamePrefix = "grocerease-";

    public const string FileNameSuffix = ".log";

    public const int MinRetainedDays = 1;
    public const int MaxRetainedDays = 365;
    public const int MinimumFileSizeKilobytes = 64;
    public const int MaximumFileSizeKilobytes = 1_048_576; // 1 GiB

    /// <summary>Where files are written. A relative path is relative to the working directory (<c>/app</c> in the image). It is created if missing.</summary>
    [Required(ErrorMessage = "LogFiles:Directory is required.")]
    public string Directory { get; init; } = string.Empty;

    /// <summary>How many days of files are kept. Older files are deleted when the next one is opened.</summary>
    [Range(MinRetainedDays, MaxRetainedDays, ErrorMessage = "LogFiles:RetainedDays must be between 1 and 365.")]
    public int RetainedDays { get; init; }

    /// <summary>
    /// How large one file may grow. A day's file that reaches it is closed and a numbered one is started, so one noisy day (or an
    /// attack) cannot fill the disk with a single file that retention could not touch until its day was over. The size is checked
    /// after each event is written, so a file can end up larger than this by at most its last event.
    /// </summary>
    [Range(MinimumFileSizeKilobytes, MaximumFileSizeKilobytes, ErrorMessage = "LogFiles:MaxFileSizeKilobytes must be between 64 and 1048576 (1 GiB).")]
    public int MaxFileSizeKilobytes { get; init; }
}
