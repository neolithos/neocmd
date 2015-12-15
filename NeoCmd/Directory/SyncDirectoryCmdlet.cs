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
		private readonly CmdletNotify notify;

		public SyncDirectoryCmdlet()
		{
			this.notify = new CmdletNotify(this);
		} // ctor

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
			bar.StatusText = $"Copy:{source.FullName} -> {target.FullName}";
	
			// update attributes
			if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
				target.Attributes = FileAttributes.Normal;

			notify.SafeIO(() => target = source.CopyTo(target.FullName, target.Exists), bar.StatusText);

			// update attributes
			if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
				target.Attributes = FileAttributes.Normal;

			// copy attributes
			target.CreationTimeUtc = source.CreationTimeUtc;
			target.LastAccessTimeUtc = source.LastAccessTimeUtc;
			target.LastWriteTimeUtc = source.LastWriteTimeUtc;
			target.Attributes = source.Attributes;
		} // proc SyncFileItem

		private void SyncRemoveItem(CmdletProgress bar, FileSystemInfo fsi)
		{
			var directoryInfo = fsi as DirectoryInfo;
			if (directoryInfo != null)
			{
				foreach (var cur in directoryInfo.EnumerateFileSystemInfos())
				{
					SyncRemoveItem(bar, cur);
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
			bar.StatusText = $"Delete: {fsi.FullName}";
			notify.SafeIO(fsi.Delete, bar.StatusText);
		} // proc SyncRemoveItem

		private void SyncItems(CmdletProgress bar, DirectoryInfo source, DirectoryInfo target)
		{
			WriteVerbose($"Enter directory: {source.FullName}");

      if (!source.Exists)
				throw new ArgumentException($"Path not found ({source.FullName}).");

			if (!target.Exists)
			{
				bar.StatusText = $"Create directory: {target}";
				target.Create();
			}

			// Hole die Dateilisten ab
			var sourceItems = source.GetFileSystemInfos();
			var targetItems = target.GetFileSystemInfos();

			// Vergleiche die Listen
			for (var i = 0; i < sourceItems.Length; i++)
			{
				var fsi = sourceItems[i];
				var index = Array.FindIndex(targetItems, c => c != null && string.Compare(c.Name, fsi.Name, true) == 0);
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
					this.SyncRemoveItem(bar, fsi);
			}

			WriteVerbose($"Leave directory: {target.FullName}");
    } // proc SyncItems

		protected override void ProcessRecord()
		{
			using (var bar = notify.CreateStatus(String.Format("Synchronize {0} -> {1}", Source, Target), String.Empty))
				SyncItems(bar, new DirectoryInfo(Source), new DirectoryInfo(Target));
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
