using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;

namespace Neo.PowerShell.Backup
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsData.Backup, "Directory")]
	public sealed class BackupDirectoryCmdlet : NeoCmdlet
	{
		#region -- ProcessRecord ----------------------------------------------------------

		protected override void ProcessRecord()
		{
			using (var bar = Notify.CreateStatus("Erzeuge Backup", $"Sicherung von {Source}..."))
			{
				var totalBytes = 0L;
				//var position = 0L;
				var itemsModified = 0;
				var itemsUnmodified = 0;
				var itemsZipped = 0;
				var archiveUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

				// Lade Index-Datei
				bar.StatusText = "Lese Index...";
				var targetPath = new DirectoryInfo(Target);
				var targetIndex = Path.Combine(targetPath.FullName, "index.txt.gz");
				var index = new FileIndex();
				if (String.IsNullOrEmpty(ShadowIndex)) // Kein lokaler Index, also lade dem vom Target
					index.ReadIndex(Notify, targetIndex);
				else
					index.ReadIndex(Notify, ShadowIndex);

				// Erzeuge den Archivnamen für die neuen Dateien
				var zipArchiveName = Guid.NewGuid().ToString("N") + ".zip";

				// Gleiche die Daten ab und erzeuge die Statistik
				bar.StatusText = "Vergleiche Dateien mit Index...";
				var swFileStopWatch = Stopwatch.StartNew();
				var files = new FileList(Notify, new DirectoryInfo(Source), Excludes);
				foreach (var c in files)
				{
					var indexItem = index.UpdateFile(c);
					var tmp = 0;

					// Gib einen zwischen Bericht
					if (swFileStopWatch.ElapsedMilliseconds > 500)
					{
						bar.StatusText = $"Vergleiche {c.RelativePath} mit Index...";
						swFileStopWatch = Stopwatch.StartNew();
					}

					switch (indexItem.State)
					{
						case FileIndexState.Modified:
							// Erzeuge den Eintrag im Index
							if (c.FileInfo.Length < ZipArchiveBorder)
							{
								itemsZipped++;
								indexItem.ArchiveName = zipArchiveName;
							}
							else
							{
								if (String.IsNullOrEmpty(indexItem.ArchiveName))
									indexItem.ArchiveName = Guid.NewGuid().ToString("N") + Path.GetExtension(indexItem.RelativePath) + (ZipFile(indexItem.RelativePath) ? ".gz" : ".nopack");
							}

							// Statistik für den Progress
							totalBytes += c.FileInfo.Length;
							itemsModified++;

							// Erhöhe den Zugriff
							if (archiveUsed.TryGetValue(indexItem.ArchiveName, out tmp))
								archiveUsed[indexItem.ArchiveName] = tmp + 1;
							break;
						case FileIndexState.Unmodified:
							// Prüfe die existens den Archives
							if (Force || (String.IsNullOrEmpty(ShadowIndex) && !File.Exists(Path.Combine(targetPath.FullName, indexItem.ArchiveName))))
							{
								indexItem.Update(c.FileInfo);
								goto case FileIndexState.Modified;
							}
							itemsUnmodified++;

							// Erhöhe den Zugriff
							if (archiveUsed.TryGetValue(indexItem.ArchiveName, out tmp))
								archiveUsed[indexItem.ArchiveName] = tmp + 1;
							break;
						case FileIndexState.None:
							if (archiveUsed.ContainsKey(indexItem.ArchiveName))
								archiveUsed[indexItem.ArchiveName] = 0;
							break;
					}
				}

				// Schreibe das neue Archiv
				if (itemsModified > 0)
				{
					FileWrite zipStream = null;
					ZipOutputStream zip = null;
					try
					{
						if (itemsZipped > 0)
						{
							zipStream = new FileWrite(Notify, new FileInfo(Path.Combine(targetPath.FullName, zipArchiveName)), true, CompressMode.Stored);
							zip = new ZipOutputStream(zipStream.Stream);
							zip.UseZip64 = UseZip64.On;
							zip.SetLevel(5);
						}

						bar.StartRemaining();
						bar.Maximum = totalBytes;

						var removeItems = new List<FileIndexItem>();
						foreach (var c in index)
						{
							switch (c.State)
							{
								case FileIndexState.Modified: // Kopiere die Datei
									using (var src = Stuff.OpenRead(new FileInfo(Path.Combine(Source, c.RelativePath)), Notify, allowEmpty: true))
									{
										if (c.ArchiveName == zipArchiveName)
											ZipFileItem(Notify, bar, src, zip, c);
										else
											GZipFileItem(Notify, bar, src, targetPath, c);
									}
									break;
								case FileIndexState.None: // Lösche den Index
									removeItems.Remove(c);
									break;
							}
						}

						// Entferne die Einträge aus dem Index
						foreach (var c in removeItems)
							index.RemoveEntry(c);

						zipStream.Commit();
					}
					finally
					{
						if (zip != null)
						{
							zip.Flush();
							zip.Dispose();
						}
						if (zipStream != null)
							zipStream.Dispose();
					}

					// Schreibe den Index
					bar.StopRemaining();
					bar.StatusText = "Schreibe Index...";
					if (!String.IsNullOrEmpty(ShadowIndex))
						index.WriteIndex(Notify, ShadowIndex);
					index.WriteIndex(Notify, targetIndex);

					// Lösche ungenutzte Archive
					if (String.IsNullOrEmpty(ShadowIndex))
					{
						foreach (var c in archiveUsed)
							if (c.Value == 0)
							{
								var file = new FileInfo(Path.Combine(targetPath.FullName, c.Key));
								bar.StatusText = $"Nicht mehr benötigtes Archiv '{c.Key}'...";
								Notify.SafeIO(file.Delete, bar.StatusText);
							}
					}
					else // Erzeuge nur eine Löschdatei
					{
						bar.StatusText = "Nicht mehr benötigte Archive werden gelöscht...";
						using (var sw = new StreamWriter(Stuff.OpenWrite(new FileInfo(Path.Combine(targetPath.FullName, "index_rm.txt")), Notify, CompressMode.Stored)))
						{
							sw.BaseStream.Position = sw.BaseStream.Length;
							foreach (var c in archiveUsed)
								if (c.Value == 0)
									sw.WriteLine(c.Key);
						}
					}
				}
			}
		} // proc ProcessRecord

		private void ZipFileItem(CmdletNotify notify, CmdletProgress bar, Stream src, ZipOutputStream zip, FileIndexItem item)
		{
			var entry = new ZipEntry(ZipEntry.CleanName(item.RelativePath));

			entry.DateTime = item.LastWriteTimeUtc;
			entry.Size = src.Length;
			entry.Comment = item.GetComment();
			entry.CompressionMethod = ZipFile(item.RelativePath) ? CompressionMethod.Deflated : CompressionMethod.Stored;
			zip.PutNextEntry(entry);

			Stuff.CopyRawBytes(bar, item.RelativePath, src.Length, src, zip);

			zip.CloseEntry();
			zip.Flush();
		} // proc ZipFileItem

		private void GZipFileItem(CmdletNotify notify, CmdletProgress bar, Stream src, DirectoryInfo targetPath, FileIndexItem item)
		{
			using (var dst = new FileWrite(notify, new FileInfo(Path.Combine(targetPath.FullName, item.ArchiveName)), false, CompressMode.Auto))
			{
				Stuff.CopyRawBytes(bar, item.RelativePath, src.Length, src, dst.Stream);
				dst.Commit();
			}
		} // proc GZipFileItem

		private bool ZipFile(string name)
		{
			var noCompress = NoCompress;
			if (noCompress == null || noCompress.Length == 0)
				return true;

			var ext = Path.GetExtension(name);

			for (int i = 0; i < noCompress.Length; i++)
			{
				if (name.EndsWith(noCompress[i]))
					return false;
			}
			return true;
		} // func ZipFile

		#endregion

		#region -- Arguments --------------------------------------------------------------

		[
		Parameter(Mandatory = true, Position = 0, HelpMessage = "Verzeichnis, welches gesichert werden soll."),
		Alias("path")
		]
		public string Source { get; set; }
		[
		Parameter(Mandatory = true, Position = 1, HelpMessage = "Verzeichnis, welches das Backup aufnimmt.")
		]
		public string Target { get; set; }

		[
		Parameter(Mandatory = false, Position = 3, HelpMessage = "Es wird für den abgleich dieser lokale Index verwendet.")
		]
		public string ShadowIndex { get; set; }
		[
		Parameter(Mandatory = false, HelpMessage = "Führt keinen Abgleich aus, sondern sichert alle Dateien erneut.")
		]
		public bool Force { get; set; } = false;

		[
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien die ausgeschlossen werden sollen.")
		]
		public string[] Excludes { get; set; }

		[
		Parameter(Mandatory = false, HelpMessage = "Ab welcher größe sollen die Dateien einzeln abgelegt werden.")
		]
		public long ZipArchiveBorder { get; set; } = 50 << 20; // 50mb
		[
		Parameter(Mandatory = false, HelpMessage = "Diese Endungen sollen nicht komprimiert werden.")
		]
		public string[] NoCompress { get; set; } = new string[] { ".jpg", ".gz", ".zip", ".7z", ".mp3", ".ac3" };

		#endregion
	} // class BackupDirectoryCmdlet
}
