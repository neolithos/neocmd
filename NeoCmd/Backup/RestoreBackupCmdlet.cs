using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using ICSharpCode.SharpZipLib.Zip;

namespace Neo.PowerShell.Backup
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsData.Restore, "directory")]
	public sealed class RestoreBackupCmdlet : NeoCmdlet
	{
		#region -- ProcessRecord ----------------------------------------------------------

		private static void AddArchive(Dictionary<string, List<FileIndexItem>> archives, FileIndexItem item, ref long totalBytes)
		{
			List<FileIndexItem> items;
			if (!archives.TryGetValue(item.ArchiveName, out items))
				archives[item.ArchiveName] = items = new List<FileIndexItem>();
			items.Add(item);
			totalBytes += item.Length;
		} // proc AddArchive

		private void UpdateMetaData(CmdletNotify notify, FileIndexItem src, FileInfo dst)
		{
			notify.SafeIO(() =>
			{
				dst.LastAccessTimeUtc = src.LastAccessTimeUtc;
				dst.LastWriteTimeUtc = src.LastWriteTimeUtc;
				dst.CreationTimeUtc = src.CreationTimeUtc;
				dst.Attributes = src.Attributes;
			}, $"Setzen der Attribute {src.RelativePath} ist fehlgeschlagen.");
		} // proc UpdateMetaData

		protected override void ProcessRecord()
		{
			var notify = new CmdletNotify(this);
			var totalBytes = 0L;

			// Lese den Index ein
			var index = new FileIndex();
			index.ReadIndex(notify, Path.Combine(Source, "index.txt.gz"));

			// Suche alle aktiven Archive
			var archives = new Dictionary<string, List<FileIndexItem>>(StringComparer.OrdinalIgnoreCase);

			using (var bar = notify.CreateStatus("Wiederherstellen eines Verzeichnisses", $"Wiederherstellen von {Source}..."))
			{
				var filter = new FileFilterRules(Filter); // Erzeuge Filter und entferne alle Dateien aus dem Index die dem nicht entsprechen
				if (filter.IsEmpty)
				{
					foreach (var c in index)
						AddArchive(archives, c, ref totalBytes);
				}
				else
				{
					var remove = new List<FileIndexItem>();
					foreach (var c in index)
						if (filter.IsFiltered(c.RelativePath))
							AddArchive(archives, c, ref totalBytes);
						else
							remove.Add(c);

					foreach (var c in remove)
						index.RemoveEntry(c);
				}

				// Entpacke die Archvie
				bar.StartRemaining();
				bar.Maximum = totalBytes;

				foreach (var c in archives)
				{
					if (Stuff.IsGZipFile(c.Key) || Stuff.IsNoPackFile(c.Key)) // GZip-Datei
					{
						using (var src = Stuff.OpenRead(new FileInfo(Path.Combine(Source, c.Key)), notify, CompressMode.Auto))
						{
							var srcFile = c.Value[0];
							var dstFile = new FileInfo(Path.Combine(Target, srcFile.RelativePath));
							using (var dst = Override ? Stuff.OpenWrite(dstFile, notify) : Stuff.OpenCreate(dstFile, notify))
							{
								dst.SetLength(srcFile.Length);
								Stuff.CopyRawBytes(bar, srcFile.RelativePath, srcFile.Length, src, dst);
							}

							// Aktualisiere die Attribute
							UpdateMetaData(notify, srcFile, dstFile);
						}
					}
					else // zip-Datei
					{
						using (var zipStream = Stuff.OpenRead(new FileInfo(Path.Combine(Source, c.Key)), notify, CompressMode.Stored))
						using (var zip = new ZipInputStream(zipStream))
						{
							var srcEntry = zip.GetNextEntry();
							while (srcEntry != null)
							{
								// Suche den passenden Index
								var srcIndex = c.Value.Find(c2 => String.Compare(srcEntry.Name, ZipEntry.CleanName(c2.RelativePath), StringComparison.OrdinalIgnoreCase) == 0);
								if (srcIndex != null)
								{
									var dstFile = new FileInfo(Path.Combine(Target, srcIndex.RelativePath));
									using (var dst = Override ? Stuff.OpenWrite(dstFile, notify) : Stuff.OpenCreate(dstFile, notify))
									{
										dst.SetLength(srcIndex.Length);
										Stuff.CopyRawBytes(bar, srcIndex.RelativePath, srcIndex.Length, zip, dst);
									}

									// Aktualisiere die Attribute
									UpdateMetaData(notify, srcIndex, dstFile);
								}
								else
									zip.CloseEntry();

								// Schließe den Eintrag ab
								srcEntry = zip.GetNextEntry();
							}
						}
					}
				}
			}
		} // proc ProcessRecord

		#endregion

		#region -- Arguments --------------------------------------------------------------

		[
		Parameter(Position = 0, HelpMessage = "Backupverzeichnis, welches wiederhergestellt werden soll.")
		]
		public string Source { get; set; }
		[
		Parameter(Position = 1, HelpMessage = "Verzeichnis, in welches die Dateien wiederhergestellt werden sollen.")
		]
		public string Target { get; set; }
		[
		Parameter(HelpMessage = "Soll gefragt werden bevor eine Datei überschrieben wird.")
		]
		public SwitchParameter Override { get; set; } = false;
		[
		Parameter(HelpMessage = "Einträge die wiederherstellt werden sollen.")
		]
		public string[] Filter { get; set; } = null;

		#endregion
	} // class RestoreBackupCmdlet
}
