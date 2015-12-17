using System;
using System.IO;
using System.Management.Automation;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsData.Sync, "directory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
	public sealed class SyncDirectoryCmdlet : NeoCmdlet
	{
		private FileFilterRules rules;
		
		private int sourceOffset;
		private int targetOffset;

		#region -- ProcessRecord ----------------------------------------------------------

		private void SyncFileItem(CmdletProgress bar, FileInfo source, FileInfo target)
		{
			// is the file changed
			if (target.Exists &&
				source.Length == target.Length &&
				Stuff.CompareFileTime(source.CreationTimeUtc, target.CreationTimeUtc) &&
				Stuff.CompareFileTime(source.LastAccessTimeUtc, target.LastAccessTimeUtc) &&
				Stuff.CompareFileTime(source.LastWriteTimeUtc, target.LastWriteTimeUtc))
				return;

			// copy the file
			if (ShouldProcess(source.FullName, "sync"))
			{
				bar.StatusText = $"Copy:{source.FullName} -> {target.FullName}";

				// update attributes
				if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
					target.Attributes = FileAttributes.Normal;

				Notify.SafeIO(() => target = source.CopyTo(target.FullName, target.Exists), bar.StatusText);

				// update attributes
				if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
					target.Attributes = FileAttributes.Normal;

				// copy attributes
				target.CreationTimeUtc = source.CreationTimeUtc;
				target.LastAccessTimeUtc = source.LastAccessTimeUtc;
				target.LastWriteTimeUtc = source.LastWriteTimeUtc;
				target.Attributes = source.Attributes;
			}
		} // proc SyncFileItem

		private void SyncRemoveItem(CmdletProgress bar, FileSystemInfo fsi, int relativeOffset)
		{
			var directoryInfo = fsi as DirectoryInfo;
			if (directoryInfo != null)
			{
				foreach (var cur in directoryInfo.EnumerateFileSystemInfos())
				{
					SyncRemoveItem(bar, cur, relativeOffset);
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
			if (ShouldProcess(GetRelativePath(fsi.FullName, relativeOffset), "remove"))
				Notify.SafeIO(fsi.Delete, $"Delete: {fsi.FullName}");
		} // proc SyncRemoveItem

		private void SyncItems(CmdletProgress bar, DirectoryInfo source, DirectoryInfo target)
		{
			bar.StatusText = $"Scan directory: {source.FullName}";

			//WriteVerbose($"Enter directory: {source.FullName}");
			if (!source.Exists)
				throw new ArgumentException($"Path not found ({source.FullName}).");

			if (!target.Exists)
			{
				if (ShouldProcess(target.FullName, "create directory"))
				{
					target.Create();
					target.Refresh();
				}
			}

			// build filter
			rules = new FileFilterRules(Excludes);
			
			// get the files of the current directories
			var sourceItems = source.GetFileSystemInfos();
			var targetItems = target.Exists ? target.GetFileSystemInfos() : new FileSystemInfo[0];

			// compare
			for (var i = 0; i < sourceItems.Length; i++)
			{
				var fsi = sourceItems[i];

				// is this path filtered
				var currentSourcePath = GetRelativePath(fsi.FullName, sourceOffset);
				if (rules.IsFiltered(currentSourcePath))
				{
					WriteVerbose($"Skip: {currentSourcePath}");
					continue;
				}

				var index = Array.FindIndex(targetItems, c => c != null && String.Compare(c.Name, fsi.Name, StringComparison.OrdinalIgnoreCase) == 0);
				if (fsi is DirectoryInfo)
					SyncItems(bar, (DirectoryInfo)fsi, new DirectoryInfo(Path.Combine(target.FullName, fsi.Name)));
				else if (index != -1)
					SyncFileItem(bar, (FileInfo)fsi, (FileInfo)targetItems[index]);
				else
					SyncFileItem(bar, (FileInfo)fsi, new FileInfo(Path.Combine(target.FullName, fsi.Name)));

				if (index != -1)
					targetItems[index] = null;

				if (Stopping)
					return;
			}

			// Lösche nicht mehr vorhandene Items
			for (var j = 0; j < targetItems.Length; j++)
			{
				var fsi = targetItems[j];
				if (fsi != null)
					this.SyncRemoveItem(bar, fsi, targetOffset);
			}

			//WriteVerbose($"Leave directory: {target.FullName}");
    } // proc SyncItems

		private static string GetCleanPath(string path)
		{
			if (String.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");

			if (path.EndsWith("\\"))
				path = path.Substring(0, path.Length - 1);

			return path;
		} // func GetCleanPath

		private static string GetRelativePath(string path, int offset)
			=> path.Substring(offset);

		protected override void ProcessRecord()
		{
			using (var bar = Notify.CreateStatus(String.Format("Synchronize {0} -> {1}", Source, Target), String.Empty))
			{
				var source = new DirectoryInfo(GetCleanPath(Source));
				var target = new DirectoryInfo(GetCleanPath(Target));
				sourceOffset = source.FullName.Length + 1;
				targetOffset = target.FullName.Length + 1;
				SyncItems(bar, source, target);
			}
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
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien, die ausgeschlossen werden sollen.")
		]
		public string[] Excludes { get; set; }

		#endregion
	} // class SyncDirectoryCmdlet
}
