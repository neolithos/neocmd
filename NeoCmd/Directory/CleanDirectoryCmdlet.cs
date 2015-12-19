using System;
using System.IO;
using System.Management.Automation;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsCommon.Clear, "directory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
	public sealed class CleanDirectoryCmdlet : NeoCmdlet
	{
		private long bytesDeleted;
		private int filesDeleted;
		private int directoriesDeleted;

		#region -- ProcessRecord ----------------------------------------------------------

		private bool IsOutAged(DateTime dtNow, DateTime dt, TimeSpan t)
		{
			return dt + t < dtNow;
		} // func IsOutAged

		private bool CleanDirectoryPath(CmdletProgress bar, DateTime dtNow, TimeSpan age, DirectoryInfo currentDirecotry, string relativePath)
		{
			var empty = true;
			foreach (var fsi in currentDirecotry.EnumerateFileSystemInfos())
			{
				var currentRelativePath = Path.Combine(relativePath, fsi.Name);
				var di = fsi as DirectoryInfo;
				var fi = fsi as FileInfo;
				if (di != null)
				{
					if (CleanDirectoryPath(bar, dtNow, age, di, currentRelativePath))
					{
						directoriesDeleted++;
						empty = DeleteSafe(bar, di.Delete, currentRelativePath);
					}
					else
						empty = false;
				}
				else if (fi != null)
				{
					if (IsOutAged(dtNow, fi.LastAccessTime, age) || IsOutAged(dtNow, fi.LastWriteTime, age) || IsOutAged(dtNow, fi.LastAccessTime, age))
					{
						bytesDeleted += fi.Length;
						filesDeleted++;
						if (!DeleteSafe(bar, fi.Delete, currentRelativePath))
							empty = false;
					}
					else
						empty = false;
				}
			}
			return empty;
		} // proc CleanDirectory

		private bool DeleteSafe(CmdletProgress bar, Action delete, string relativePath)
		{
			try
			{
				if (ShouldProcess(relativePath, "remove"))
				{
					bar.CurrentOperation = $"Delete {relativePath}...";
					delete();
				}
				return true;
			}
			catch (Exception e)
			{
				WriteWarning($"Delete failed: {relativePath} ([{e.GetType().Name}] {e.Message})");
        return false;
			}
		} // proc DeleteSafe

		protected override void ProcessRecord()
		{
			directoriesDeleted = 0;
			filesDeleted = 0;
			bytesDeleted = 0;

			using (var bar = Notify.CreateStatus("Clean directory", $"Clean {Target}..."))
				CleanDirectoryPath(bar, DateTime.Now, Age.TotalMilliseconds > 0 ? Age : Age.Negate(), new DirectoryInfo(Target), String.Empty);

			WriteVerbose($"Removed: {directoriesDeleted:N0} directories, {filesDeleted:N0} files, {Stuff.FormatFileSize(bytesDeleted)}");
		} // proc ProcessRecord

		#endregion

		#region -- Arguments --------------------------------------------------------------

		[
		Parameter(Mandatory = true, Position = 0, HelpMessage = "Verzeichnis, welches gesäubert werden soll."),
		Alias("path")
		]
		public string Target { get; set; }
		[
		Parameter(Mandatory = false, Position = 1, HelpMessage = "Alter der Datei bevor Sie gelöscht wird. Am z.B. -7 für sieben Tage.")
		]
		public TimeSpan Age { get; set; } = TimeSpan.FromDays(-7);
		[
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien, die ausgeschlossen werden sollen.")
		]
		public string[] Excludes { get; set; }

		#endregion
	} // class CleanDirectoryCmdlet
}
