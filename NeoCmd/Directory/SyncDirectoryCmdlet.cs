using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsData.Sync, "directory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
	public sealed class SyncDirectoryCmdlet : NeoCmdlet
	{
		private static readonly FileSystemInfo[] emptyFileSystemInfos = new FileSystemInfo[0];
		private FileFilterRules rules;
		
		private int sourceOffset;
		private int targetOffset;

		private CmdletProgress bar;
		private int lastScanEmitted = Environment.TickCount;
		private bool copyStarted = false;

		#region -- RemoveItem, CopyItem ---------------------------------------------------

		private void RemoveItem(FileSystemInfo fsi)
		{
			var directoryInfo = fsi as DirectoryInfo;
			if (directoryInfo != null)
			{
				foreach (var cur in directoryInfo.EnumerateFileSystemInfos())
				{
					RemoveItem(cur);
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
			if (ShouldProcess(GetRelativePath(fsi.FullName, targetOffset), "remove"))
				Notify.SafeIO(fsi.Delete, $"Delete: {fsi.FullName}");
		} // proc RemoveItem

		private void CopyItem(FileInfo source, FileInfo target)
		{
			// check the target directory
			if (!target.Directory.Exists)
			{
				if (ShouldProcess(target.Directory.FullName, "create directory"))
				{
					target.Directory.Create();
					target.Refresh();
				}
			}

			if (ShouldProcess(target.FullName, "copy file"))
			{
				// update attributes
				if (target.Exists && (target.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes)0)
					target.Attributes = FileAttributes.Normal;

				// copy the file data
				using (var src = source.OpenRead(Notify, allowEmpty: true))
				using (var dst = target.OpenWrite(Notify))
					Stuff.CopyRawBytes(bar, GetRelativePath(target.FullName, targetOffset), source.Length, src, dst);

				// update dates
				target.CreationTimeUtc = source.CreationTimeUtc;
				target.LastAccessTimeUtc = source.LastAccessTimeUtc;
				target.LastWriteTimeUtc = source.LastWriteTimeUtc;
				target.Attributes = source.Attributes;

				target.SetAccessControl(source.GetAccessControl());
			}
		} // proc CopyItem

		#endregion

		#region -- CompareFile, CompareDirectory ------------------------------------------

		private void CompareFile(FileInfo source, FileInfo target)
		{
			if (target.Exists &&
					source.Length == target.Length &&
					Stuff.CompareFileTime(source.CreationTimeUtc, target.CreationTimeUtc) &&
					Stuff.CompareFileTime(source.LastAccessTimeUtc, target.LastAccessTimeUtc) &&
					Stuff.CompareFileTime(source.LastWriteTimeUtc, target.LastWriteTimeUtc))
				return;

			copyStarted = true;
			bar.AddSilentMaximum(source.Length); // update progress
			EnqueueAction(() => CopyItem(source, target));
		} // func CompareFile

		private void CompareDirectory(DirectoryInfo source, DirectoryInfo target)
		{
			if (!source.Exists)
				throw new ArgumentException($"Path not found ({source.FullName}).");

			// get the files of the current directories
			var sourceItems = GetFileItems(source);
			var targetItems = target.Exists ? target.GetFileSystemInfos() : emptyFileSystemInfos;

			if (!copyStarted)
			{
				if (unchecked(Environment.TickCount - lastScanEmitted) > 500)
				{
					EnqueueAction(() => bar.CurrentOperation = $"Scan: {GetRelativePath(source.FullName, sourceOffset) }");
					lastScanEmitted = Environment.TickCount;
				}
			}

			// compare
			for (var i = 0; i < sourceItems.Length; i++)
			{
				var fsi = sourceItems[i];

				// is this path filtered
				var currentSourcePath = GetRelativePath(fsi.FullName, sourceOffset);
				if (rules.IsFiltered(currentSourcePath))
				{
					EnqueueAction(() => WriteVerbose($"Skip: {currentSourcePath}"));
					continue;
				}

				// compare the file items
				var index = Array.FindIndex(targetItems, c => c != null && String.Compare(c.Name, fsi.Name, StringComparison.OrdinalIgnoreCase) == 0);
				if (fsi is DirectoryInfo)
				{
					if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0)
						continue; // skip reparse points

					if (index == -1)
						CompareDirectory((DirectoryInfo)fsi, new DirectoryInfo(Path.Combine(target.FullName, fsi.Name)));
					else
						CompareDirectory((DirectoryInfo)fsi, (DirectoryInfo)targetItems[index]);
				}
				else
				{
					if (index == -1)
						CompareFile((FileInfo)fsi, new FileInfo(Path.Combine(target.FullName, fsi.Name)));
					else
						CompareFile((FileInfo)fsi, (FileInfo)targetItems[index]);
				}

				if (index != -1)
					targetItems[index] = null;

				if (Stopping)
					return;
			}

			// Remove items that are not touched
			for (var j = 0; j < targetItems.Length; j++)
			{
				var fsi = targetItems[j];
				if (fsi != null)
					EnqueueAction(() => RemoveItem(fsi));
			}
		} // func CompareDirectory

		private static FileSystemInfo[] GetFileItems(DirectoryInfo source)
		{
			try
			{
				return source.GetFileSystemInfos();
			}
			catch (UnauthorizedAccessException)
			{

				return new FileSystemInfo[0];
			}
		} // func GetFileItems

		private void CompareDirectoryRoot()
		{
			try
			{
				var source = new DirectoryInfo(Stuff.GetCleanPath(Source));
				var target = new DirectoryInfo(Stuff.GetCleanPath(Target));

				sourceOffset = source.FullName.Length + 1;
				targetOffset = target.FullName.Length + 1;

				// build filter
				rules = new FileFilterRules(Excludes);

				CompareDirectory(source, target);
			}
			finally
			{
				EnqueueAction(null);
			}
		} // func CompareDirectoryRoot

		#endregion

		protected override void ProcessRecord()
		{
			using (bar = Notify.CreateStatus(String.Format("Synchronize {0} -> {1}", Source, Target), String.Empty))
			{
				bar.Maximum = 0;

				// start compare
				Task t = Task.Factory.StartNew(CompareDirectoryRoot);

				// copy files
				bar.StartRemaining();
				DequeueActions(true);
				if (!Stopping)
					t.Wait();
			}
		} // proc ProcessRecord

		private static string GetRelativePath(string path, int offset)
		{
			if (offset < path.Length)
				return path.Substring(offset);
			else
				return "\\";
		} // func GetRelativePath

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
