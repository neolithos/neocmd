using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsCommon.Clear, "directory")]
	public sealed class CleanDirectoryCmdlet : NeoCmdlet
	{
		#region -- ProcessRecord ----------------------------------------------------------

		private bool IsOutAged(DateTime dtNow, DateTime dt, TimeSpan t)
		{
			return dt + t < dtNow;
		} // func IsOutAged

		private bool CleanDirectoryPath(CmdletNotify notify, CmdletProgress bar, DateTime dtNow, TimeSpan age, DirectoryInfo currentDirecotry, string relativePath)
		{
			var empty = true;
			foreach (var fsi in currentDirecotry.EnumerateFileSystemInfos())
			{
				var currentRelativePath = Path.Combine(relativePath, fsi.Name);
				var di = fsi as DirectoryInfo;
				var fi = fsi as FileInfo;
				if (di != null)
				{
					if (CleanDirectoryPath(notify, bar, dtNow, age, di, currentRelativePath))
						try
						{
							di.Delete();
						}
						catch (IOException)
						{
							WriteWarning($"{currentRelativePath} nicht gelöscht.");
							empty = false;
						}
					else
						empty = false;
				}
				else if (fi != null)
				{
					if (IsOutAged(dtNow, fi.LastAccessTime, age) || IsOutAged(dtNow, fi.LastWriteTime, age) || IsOutAged(dtNow, fi.LastAccessTime, age))
					{
						bar.StatusText = $"Lösche {currentRelativePath}...";
						try
						{
							fi.Delete();
						}
						catch (IOException)
						{
							WriteWarning($"{currentRelativePath} nicht gelöscht.");
							empty = false;
						}
					}
					else
						empty = false;
				}
			}
			return empty;
		} // proc CleanDirectory

		protected override void ProcessRecord()
		{
			var notify = new CmdletNotify(this);
			using (var bar = notify.CreateStatus("Säubere Verzeichnis", $"{Target} wird gesäubert..."))
				CleanDirectoryPath(notify, bar, DateTime.Now, Age.TotalMilliseconds > 0 ? Age : Age.Negate(), new DirectoryInfo(Target), String.Empty);
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
