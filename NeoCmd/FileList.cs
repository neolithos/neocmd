using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Neo.PowerShell
{
	#region -- class FileFilterRules ----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Filterliste für Dateien</summary>
	public sealed class FileFilterRules
	{
		private static readonly Regex[] regexEmpty = new Regex[0];

		private Regex[] filterRules;   // Regelwerk

		/// <summary>Erzeugt eine Dateifilterliste</summary>
		/// <param name="filter">Filterdefinitionen</param>
		public FileFilterRules(params string[] filter)
		{
			this.filterRules = filter == null ? regexEmpty : (from cur in filter select CreatePathRegex(cur)).Where(c => c != null).ToArray();
		} // ctor

		private Regex CreatePathRegex(string value)
		{
			if (String.IsNullOrEmpty(value))
				return null;

			if (value[0] == '$')
				return new Regex(value.Substring(1), RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10));
			else
			{
				var regex = new StringBuilder("^");
				for (int i = 0; i < value.Length; i++)
				{
					var c = value[i];
					if (c == '*')
						regex.Append(".*");
					else if (c == '\\')
						regex.Append(@"\\");
					else
						regex.Append(c);
				}

				return new Regex(regex.ToString(), RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
			}
		} // func CreatePathRegex

		/// <summary>Ist die Datei mit dem Filter beschrieben wurden.</summary>
		/// <param name="relativePath">Pfad zu der Datei.</param>
		/// <returns></returns>
		public bool IsFiltered(string relativePath)
		{
			for (int i = 0; i < filterRules.Length; i++)
			{
				if (filterRules[i].IsMatch(relativePath))
					return true;
			}
			return false;
		} // func IsFiltered

		/// <summary>Gibt es Regeln.</summary>
		public bool IsEmpty => filterRules.Length == 0;
	} // class FileFilterRules

	#endregion

	#region -- class FileListItem -------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Repräsentiert einen gefunden Eintrag, es wird zur ursprünglichen
	/// Suchanfrage der Basispfad zurückgegeben..</summary>
	public class FileListItem
	{
		private readonly string relativePath;
		private readonly FileInfo fileInfo;

		public FileListItem(string relativePath, FileInfo fileInfo)
		{
			this.relativePath = relativePath;
			this.fileInfo = fileInfo;
		} // ctor

		/// <summary>Name der Datei.</summary>
		public string Name => fileInfo.Name;
		/// <summary>Relativer Pfad zur Basis</summary>
		public string RelativePath => relativePath;
		/// <summary>Dateiinformationen</summary>
		public FileInfo FileInfo => fileInfo;
	} // FileListItem

	#endregion

	#region -- class FileList -----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class FileList : IEnumerable<FileListItem>
	{
		private CmdletNotify notify;
		private DirectoryInfo basePath;     // Verzeichnis welches gesichert werden soll
		private FileFilterRules excludes;   // Dateien nicht enthalten sein sollen

		/// <summary>Erzeugt eine Dateiliste</summary>
		/// <param name="notify"></param>
		/// <param name="basePath">Basispfad</param>
		/// <param name="excludes">Dateien die ausgeschlossen werden sollen.</param>
		public FileList(CmdletNotify notify, DirectoryInfo basePath, params string[] excludes)
		{
			this.notify = notify;
			this.basePath = basePath;
			this.excludes = new FileFilterRules(excludes);
		} // ctor

		public IEnumerable<FileSystemInfo> GetEnumFileSystemInfo(DirectoryInfo currentDirectory, string currentRelativePath)
		{
			try
			{
				if ((currentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
					return null;
				return currentDirectory.EnumerateFileSystemInfos();
			}
			catch (UnauthorizedAccessException)
			{
				notify.UI.WriteWarningLine($"Zugriff verweigert: {currentRelativePath}");
				return null;
			}
		} // func GetEnumFileSystemInfo

		public IEnumerator<FileListItem> GetEnumerator(DirectoryInfo currentDirectory, string currentRelativePath)
		{
			var eFiles = GetEnumFileSystemInfo(currentDirectory, currentRelativePath);
			if (eFiles != null)
			{
				foreach (var fsi in eFiles)
				{
					var relativePath = Path.Combine(currentRelativePath, fsi.Name);

					// is the item filtered
					if (!excludes.IsEmpty && excludes.IsFiltered(relativePath))
						continue;

					if (fsi is FileInfo)
						yield return new FileListItem(relativePath, (FileInfo)fsi);
					else
					{
						var e = GetEnumerator((DirectoryInfo)fsi, relativePath);
						while (e.MoveNext())
							yield return e.Current;
					}
				}
			}
		} // func GetEnumerator

		public IEnumerator<FileListItem> GetEnumerator()
		{
			var e = GetEnumerator(basePath, String.Empty);
			while (e.MoveNext())
				yield return e.Current;
		} // func GetEnumerator

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>Verzeichnis, welches gescannt wird</summary>
		public DirectoryInfo BasePath => basePath;
	} // class FileList

	#endregion
}
