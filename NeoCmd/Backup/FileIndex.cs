using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Neo.PowerShell.Backup
{
	#region -- enum FileIndexState ------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Gibt den aktuellen Status einer Datei im Index an.</summary>
	public enum FileIndexState
	{
		/// <summary>Kein Status gesetzt.</summary>
		None,
		/// <summary>Datei wurde nicht verändert.</summary>
		Unmodified,
		/// <summary>Datei wurde geändert.</summary>
		Modified
	} // enum FileIndexState

	#endregion

	#region -- class FileIndexItem ------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Gibt einen einzelnen Index Eintrag zurück.</summary>
	public sealed class FileIndexItem
	{
		internal static readonly Type[] csvDefinition = new Type[] { typeof(string), typeof(DateTime), typeof(DateTime), typeof(DateTime), typeof(long), typeof(int), typeof(string) };

		private readonly string relativePath;
		private FileIndexState state;
		private DateTime creationTime;
		private DateTime lastAccessTime;
		private DateTime lastWriteTime;
		private int attributes;
		private long length;
		private string archiveName;

		/// <summary>Erzeugt einen Index-Eintrag aus den angegeben Daten. Wird zur Serialisierung verwendet.</summary>
		/// <param name="relativePath"></param>
		/// <param name="creationTime"></param>
		/// <param name="lastAccessTime"></param>
		/// <param name="lastWriteTime"></param>
		/// <param name="length"></param>
		/// <param name="attributes"></param>
		/// <param name="archiveName"></param>
		public FileIndexItem(string relativePath, DateTime creationTime, DateTime lastAccessTime, DateTime lastWriteTime, long length, int attributes, string archiveName)
		{
			this.state = FileIndexState.None;
			this.relativePath = relativePath;
			this.creationTime = creationTime;
			this.lastAccessTime = lastAccessTime;
			this.lastWriteTime = lastWriteTime;
			this.length = length;
			this.attributes = attributes;
			this.archiveName = archiveName;
		} // ctor

		/// <summary>Erzeugt einen Index-Eintrag.</summary>
		/// <param name="relativePath"></param>
		/// <param name="fileInfo"></param>
		public FileIndexItem(string relativePath, FileInfo fileInfo)
		{
			this.state = FileIndexState.Modified;
			this.relativePath = relativePath;
			this.archiveName = null;
			Update(fileInfo);
		} // ctor

		/// <summary>Prüft, ob sich an der Datei etwas geändert hat.</summary>
		/// <param name="fileInfo"></param>
		/// <returns></returns>
		public bool Equal(FileInfo fileInfo)
		{
			return length == fileInfo.Length &&
				Attributes == fileInfo.Attributes &&
				//Stuff.CompareFileTime(creationTime, fileInfo.CreationTimeUtc) &&
				//Stuff.CompareFileTime(lastAccessTime, fileInfo.LastAccessTimeUtc) &&
				Stuff.CompareFileTime(lastWriteTime, fileInfo.LastWriteTimeUtc);
		} // proc Equal

		/// <summary>Aktualisiert den Index-Eintrag.</summary>
		/// <param name="fileInfo"></param>
		public void Update(FileInfo fileInfo)
		{
			this.creationTime = fileInfo.CreationTimeUtc;
			this.lastAccessTime = fileInfo.LastAccessTimeUtc;
			this.lastWriteTime = fileInfo.LastWriteTimeUtc;
			this.attributes = (int)fileInfo.Attributes;
			this.length = fileInfo.Length;
			this.state = FileIndexState.Modified;
		} // proc Update

		/// <summary>Setzt den Eintrag auf unverändert.</summary>
		public void Unmodified()
		{
			this.state = FileIndexState.Unmodified;
		} // proc Unmodified

		/// <summary>Gibt die Rohdaten der Klasse zur Serialisierung zurück.</summary>
		/// <returns></returns>
		public object[] GetLineData()
		{
			return new object[]
				{
					relativePath,
					creationTime,
					lastAccessTime,
					lastWriteTime,
					length,
					attributes,
					archiveName
				};
		} // func GetLineData

		/// <summary>Gibt einen Kommentar für das Archiv zurück.</summary>
		/// <returns></returns>
		public string GetComment()
		{
			return String.Format(CultureInfo.InvariantCulture,
				"CreationTime={0}\n" +
				"LastAccessTime={1}\n" +
				"LastWriteTime={2}\n" +
				"Attributes={3}",
				CreationTimeUtc, LastAccessTimeUtc, LastWriteTimeUtc, attributes);
		} // funnc GetComment

		/// <summary>Gibt den Pfad zurück</summary>
		public string RelativePath => relativePath;
		/// <summary>Archive, which contains the latest version of the file.</summary>
		public string ArchiveName { get { return archiveName; } set { archiveName = value; } }
		/// <summary></summary>
		public DateTime CreationTimeUtc => creationTime;
		/// <summary></summary>
		public DateTime LastAccessTimeUtc => lastAccessTime;
		/// <summary></summary>
		public DateTime LastWriteTimeUtc => lastWriteTime;
		/// <summary>Attribute</summary>
		public FileAttributes Attributes => (FileAttributes)attributes;
		/// <summary>Länge</summary>
		public long Length => length;
		/// <summary></summary>
		public FileIndexState State => state;
	} // class FileIndexItem

	#endregion

	#region -- class FileIndex ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Verwaltet einen Index von Dateien.</summary>
	public sealed class FileIndex : IEnumerable<FileIndexItem>
	{
		private Dictionary<string, FileIndexItem> files;

		public FileIndex()
		{
			files = new Dictionary<string, FileIndexItem>(StringComparer.OrdinalIgnoreCase);
		} // ctor

		private FileInfo CreateIndexFileName(string fileName)
		{
			return new FileInfo(fileName);
		} // func CreateIndexFileName

		public void ReadIndex(CmdletNotify notify, string fileName)
		{
			var file = CreateIndexFileName(fileName);
			if (file.Exists)
			{
				using (var sr = new StreamReader(file.OpenRead(notify, CompressMode.Auto), Encoding.Unicode, true))
				{
					var csv = new CsvReader(sr, FileIndexItem.csvDefinition);
					while (true)
					{
						var cur = csv.ReadLine<FileIndexItem>();
						if (cur == null)
							break;

						files.Add(cur.RelativePath, cur);
					}
				}
			}
		} // proc ReadIndex

		public void WriteIndex(CmdletNotify notify, string fileName)
		{
			var file = CreateIndexFileName(fileName);
			// erzeuge den index nicht direkt
			using (var dst = new FileWrite(notify, file, false, CompressMode.Auto))
			using (var sw = new StreamWriter(dst.Stream, Encoding.Unicode))
			{
				var csv = new CsvWriter(sw, FileIndexItem.csvDefinition);
				foreach (var cur in files.Values)
					csv.WriteData(cur.GetLineData());

				dst.Commit();
			}
		} // proc WriteIndex

		/// <summary>Creates a index item.</summary>
		/// <param name="item">This item will be added to the index.</param>
		/// <returns>State of the currently added item.</returns>
		public FileIndexItem UpdateFile(FileListItem item)
		{
			FileIndexItem cur;
			if (files.TryGetValue(item.RelativePath, out cur))
			{
				if (!cur.Equal(item.FileInfo))
					cur.Update(item.FileInfo);
				else
					cur.Unmodified();
				return cur;
			}
			else
				return files[item.RelativePath] = new FileIndexItem(item.RelativePath, item.FileInfo);
		} // proc Update

		public void RemoveEntry(FileIndexItem item)
		{
			files.Remove(item.RelativePath);
		} // proc RemoveEntry

		public IEnumerator<FileIndexItem> GetEnumerator()
		{
			foreach (var cur in files.Values)
				yield return cur;
		} // func GetEnumerator

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>Current items in the index.</summary>
		public int Count => files.Count;
	} // class LocalIndex

	#endregion
}
