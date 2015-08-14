using System;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsData.Sync, "directory")]
	public sealed class SyncDirectoryCmdlet : NeoCmdlet
	{
		#region -- ProcessRecord ----------------------------------------------------------

		private void WriteLine(string sText)
		{
			if (this.Verbose)
				base.Host.UI.WriteLine(sText);
		} // proc WriteLine

		private void SyncFileItem(FileInfo source, FileInfo target)
		{
			// hat sich die Datei geändert
			if (target.Exists &&
				source.Length == target.Length &&
				Stuff.CompareFileTime(source.CreationTimeUtc, target.CreationTimeUtc) &&
				Stuff.CompareFileTime(source.LastAccessTimeUtc, target.LastAccessTimeUtc) &&
				Stuff.CompareFileTime(source.LastWriteTimeUtc, target.LastWriteTimeUtc))
				return;

			// Gleiche Datei ab
			WriteLine($"COPY: {source.FullName} --> {target.FullName}");
			if (!target.Directory.Exists)
				target.Directory.Create();

			if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
				target.Attributes = FileAttributes.Normal;

			int tries = 6;
			Retry:
			try
			{
				target = source.CopyTo(target.FullName, target.Exists);
			}
			catch (UnauthorizedAccessException ex)
			{
				if (tries-- <= 0)
				{
					throw ex;
				}
				Thread.Sleep(1000);
				goto Retry;
			}

			if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
				target.Attributes = FileAttributes.Normal;

			// Kopiere die Eigenschaften
			target.CreationTimeUtc = source.CreationTimeUtc;
			target.LastAccessTimeUtc = source.LastAccessTimeUtc;
			target.LastWriteTimeUtc = source.LastWriteTimeUtc;
			target.Attributes = source.Attributes;
		} // proc SyncFileItem

		private void SyncRemoveItem(FileSystemInfo fsi)
		{
			var directoryInfo = fsi as DirectoryInfo;
			if (directoryInfo != null)
			{
				foreach (var cur in directoryInfo.EnumerateFileSystemInfos())
				{
					SyncRemoveItem(cur);
					if (Stopping)
						return;
				}
			}
			else
			{
				var fi = fsi as FileInfo;
				if (fi != null && (fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
					fi.Attributes = FileAttributes.Normal;
			}

			// Delete file
			WriteLine($"DEL: {fsi.FullName}");
			fsi.Delete();
		} // proc SyncRemoveItem

		private void SyncItems(DirectoryInfo source, DirectoryInfo target)
		{
			if (!source.Exists)
				throw new ArgumentException(string.Format("Pfad existiert nicht ({0})...", source.FullName));

			if (!target.Exists)
				target.Create();

			// Hole die Dateilisten ab
			var sourceItems = source.GetFileSystemInfos();
			var targetItems = target.GetFileSystemInfos();

			// Vergleiche die Listen
			for (int i = 0; i < sourceItems.Length; i++)
			{
				var fsi = sourceItems[i];
				var index = Array.FindIndex(targetItems, c => c != null && string.Compare(c.Name, fsi.Name, true) == 0);
				if (fsi is DirectoryInfo)
					SyncItems((DirectoryInfo)fsi, new DirectoryInfo(Path.Combine(target.FullName, fsi.Name)));
				else if (index != -1)
					SyncFileItem((FileInfo)fsi, (FileInfo)targetItems[index]);
				else
					SyncFileItem((FileInfo)fsi, new FileInfo(Path.Combine(target.FullName, fsi.Name)));

				if (index != -1)
					targetItems[index] = null;

				if (Stopping)
					return;
			}

			// Lösche nicht mehr vorhandene Items
			for (int j = 0; j < targetItems.Length; j++)
			{
				var fsi = targetItems[j];
				if (fsi != null)
					this.SyncRemoveItem(fsi);
			}
		} // proc SyncItems

		protected override void ProcessRecord()
		{
			WriteLine(string.Format("SYNC: {0} --> {1}", Source, Target));
			SyncItems(new DirectoryInfo(Source), new DirectoryInfo(Target));
		} // proc ProcessRecord

		#endregion

		#region -- Arguments --------------------------------------------------------------

		[
		Parameter(Position = 0, Mandatory = true),
		Alias("path"),
		ValidateNotNullOrEmpty
		]
		public string Source { get; set; }

		[
		Parameter(Position = 1, Mandatory = true),
		Alias("destination", "dest"),
		ValidateNotNullOrEmpty
		]
		public string Target { get; set; }

		[
		Parameter(Position = 2, Mandatory = false),
		Alias("info")
		]
		public SwitchParameter Verbose { get; set; } = false;

		[
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien, die ausgeschlossen werden sollen.")
		]
		public string[] Excludes { get; set; }

		#endregion
	} // class SyncDirectoryCmdlet
}
