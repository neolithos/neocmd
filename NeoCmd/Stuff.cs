using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Text;
using System.Threading.Tasks;

namespace Neo.PowerShell
{
	#region -- enum CompressMode --------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal enum CompressMode
	{
		/// <summary>Automatisch anhand der Endung den Modus wählen.</summary>
		Auto,
		/// <summary>Datei wird nur gespeichert.</summary>
		Stored,
		/// <summary>Daten werden beim Schreiben gepackt.</summary>
		Compressed
	} // enum CompressMode

	#endregion

	#region -- class CsvWriter ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal class CsvWriter
	{
		private TextWriter tw;
		private Type[] types;

		public CsvWriter(TextWriter tw, params Type[] types)
		{
			this.tw = tw;
			this.types = types;
		} // ctor

		public void WriteData(params object[] values)
		{
			if (values == null)
				return;
			if (values.Length != types.Length)
				throw new ArgumentOutOfRangeException();

			for (int i = 0; i < types.Length; i++)
			{
				// convert the value
				string value;
				if (types[i] == typeof(string))
					value = Convert.ToString(values[i]);
				else
					value = Convert.ToString(Convert.ChangeType(values[i], types[i]), CultureInfo.InvariantCulture);

				// sep
				if (i > 0)
					tw.Write(';');

				// ; kommt nicht in dateinamen vor
				tw.Write(value);
			}

			tw.WriteLine();
		} // proc WriteData
	} // class CsvConverter

	#endregion

	#region -- class CsvWriter ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal class CsvReader
	{
		private TextReader tr;
		private Type[] types;

		public CsvReader(TextReader tr, params Type[] types)
		{
			this.tr = tr;
			this.types = types;
		} // ctor

		public object[] ReadLine()
		{
			var line = tr.ReadLine();
			if (line == null)
				return null;

			var data = line.Split(';');
			if (data.Length != types.Length)
				throw new ArgumentException("CSV-Zeile konnte nicht geparst werden.");

			var r = new object[types.Length];
			for (int i = 0; i < data.Length; i++)
			{
				var cell = data[i];
				if (types[i] == typeof(string))
					r[i] = cell;
				else
					r[i] = Convert.ChangeType(cell, types[i], CultureInfo.InvariantCulture);
			}
			return r;
		} // func ReadLine

		public T ReadLine<T>()
			where T : class
		{
			var args = ReadLine();
			if (args == null)
				return null;

			return (T)Activator.CreateInstance(typeof(T), args);
		} // func ReadLine
	} // class CsvReader

	#endregion

	#region -- class FileWrite ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class FileWrite : IDisposable
	{
		private CmdletNotify notify;

		private FileInfo file;
		private bool createMode;
		private CompressMode compressed;
		private FileInfo tempFile;
		private Stream stream;
		private bool commit = false;

		public FileWrite(CmdletNotify notify, FileInfo file, bool createMode, CompressMode compressed)
		{
			this.notify = notify;
			this.file = file;
			this.createMode = createMode;
			this.compressed = compressed;
			this.tempFile = new FileInfo(Path.Combine(file.Directory.FullName, Guid.NewGuid().ToString("N") + ".tmp"));

			if (compressed == CompressMode.Auto)
				this.compressed = Stuff.IsGZipFile(file.Name) ? CompressMode.Compressed : CompressMode.Stored;
		} // ctor

		public void Dispose()
		{
			if (tempFile != null)
			{
				// schließe den stream
				stream.Close();
				stream = null;

				// erzeuge die Zieldatei
				var msg = $"Abschluss der Datei '{file.Name}' fehlgeschlagen.";
				if (commit)
				{
					if (file.Exists)
						notify.SafeIO(file.Delete, msg);

					notify.SafeIO(() => tempFile.MoveTo(file.FullName), msg);

					file.Refresh();
				}
				else
					notify.SafeIO(tempFile.Delete, msg);

				tempFile = null;
			}
		} // proc Dispose

		public void Commit()
		{
			commit = true;
		} // proc Commit

		public Stream Stream
		{
			get
			{
				if (stream == null && tempFile != null)
					stream = createMode ? tempFile.OpenCreate(notify, compressed) : tempFile.OpenWrite(notify, compressed);
				return stream;
			}
		} // func Stream
	} // class FileWrite

	#endregion

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal static class Stuff
	{
		#region -- FormatFileSize ---------------------------------------------------------

		public static string FormatFileSize(long size)
		{
			if (size < 2048)
				return String.Format("{0:N0} bytes", size);
			else if (size < 2097152)
				return String.Format("{0:N0} KiB", size / 1024);
			else if (size < 1073741824)
				return String.Format("{0:N1} MiB", (float)size / 1048576);
			else if (size < 1099511627776)
				return String.Format("{0:N1} GiB", (float)size / 1073741824);
			else
				return String.Format("{0:N1} TiB", (float)size / 1099511627776);
		} // func FormatFileSize

		#endregion

		#region -- GetCleanPath -----------------------------------------------------------

		public static string GetCleanPath(string path)
		{
			if (String.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");

			if (path.EndsWith("\\"))
				path = path.Substring(0, path.Length - 1);

			return path;
		} // func GetCleanPath

		#endregion

		#region -- Create, Open -----------------------------------------------------------

		private static Stream OpenWrite(FileInfo file)
		{
			if (!file.Directory.Exists)
				file.Directory.Create();
			return file.OpenWrite();
		} // func OpenWrite

		private static Stream OpenRead(FileInfo file)
		{
			return new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		} // func OpenWrite

		/// <summary>Erzeugt die angegebene Datei (neu).</summary>
		/// <param name="file">Datei die angelegt werden soll.</param>
		/// <param name="notify">Zugriff auf die UI.</param>
		/// <param name="compressed">Soll die Datei gepackt sein.</param>
		/// <returns></returns>
		public static Stream OpenCreate(this FileInfo file, CmdletNotify notify, CompressMode compressed = CompressMode.Stored)
		{
			if (file.Exists)
			{
				var choices = new Collection<ChoiceDescription>();
				choices.Add(new ChoiceDescription("&Überschreiben"));
				choices.Add(new ChoiceDescription("&Abbrechen"));
				if (notify.UI.PromptForChoice("Überschreiben", $"Datei '{file.Name}' überschreiben?", choices, 0) != 0)
					notify.Abort();

				notify.SafeIO(file.Delete, $"Datei '{file.Name}' löschen.");
			}

			// Erzeuge die Datei im sicheren Context
			var src = notify.SafeIO(() => OpenWrite(file), $"Datei '{file.Name}' konnte nicht angelegt werden.");

			// Entpacke die Daten automatisch
			if (compressed == CompressMode.Compressed || (compressed == CompressMode.Auto && IsGZipFile(file.Name)))
				return new GZipStream(src, CompressionMode.Compress, false);
			else
				return src;
		} // func OpenCreate

		/// <summary>Öffnet die Datei zum Schreiben.</summary>
		/// <param name="file">Datei die geöffnet werden soll.</param>
		/// <param name="notify">Zugriff auf die UI.</param>
		/// <param name="compressed">Soll die Datei gepackt sein.</param>
		/// <returns></returns>
		public static Stream OpenWrite(this FileInfo file, CmdletNotify notify, CompressMode compressed = CompressMode.Stored)
		{
			// Erzeuge die Datei im sicheren Context
			var src = notify.SafeIO(() => OpenWrite(file), $"Datei '{file.Name}' konnte nicht zum Schreiben geöffnet werden.");

			// Entpacke die Daten automatisch
			if (compressed == CompressMode.Compressed || (compressed == CompressMode.Auto && IsGZipFile(file.Name)))
				return new GZipStream(src, CompressionMode.Compress, false);
			else
				return src;
		} // func OpenWrite

		/// <summary>Öffnet die Datei zum Lesen.</summary>
		/// <param name="file">Datei die geöffnet werden soll.</param>
		/// <param name="notify">Zugriff auf die UI.</param>
		/// <param name="compressed">Soll die Datei entpackt werden.</param>
		public static Stream OpenRead(this FileInfo file, CmdletNotify notify, CompressMode compressed = CompressMode.Stored, bool allowEmpty = false)
		{
			var desc = $"Datei '{file.Name}' kann nicht geöffnet werden.";
			var src = allowEmpty ?
				notify.SafeOpen(OpenRead, file, desc) :
				notify.SafeIO(() => OpenRead(file), desc);

			// Entpacke die Daten automatisch
			if (compressed == CompressMode.Compressed || (compressed == CompressMode.Auto && IsGZipFile(file.Name)))
				return new GZipStream(src, CompressionMode.Decompress, false);
			else
				return src;
		} // func OpenRead

		#endregion

		#region -- CopyRawBytes------------------------------------------------------------

		public static void CopyRawBytes(this CmdletProgress bar, string relativePath, long fileLength, Stream src, Stream dst)
		{
			var buf = new byte[4096];

			bar.StartIoSpeed();
			try
			{
				bar.CurrentOperation = $"Copy {relativePath}";
				bar.StatusDescription = $"Copy {Path.GetFileName(relativePath)} ({Stuff.FormatFileSize(fileLength)}$speed$)...";

				while (true)
				{
					var r = src.Read(buf, 0, buf.Length);
					if (r > 0)
					{
						dst.Write(buf, 0, r);
						bar.AddCopiedBytes(r);
						bar.Position += r;
					}
					else
					{
						if (dst.CanSeek) // cut file
							dst.SetLength(dst.Position);
						break;
					}
				}
			}
			finally
			{
				bar.StopIoSpeed();
			}
		} // proc CopyRawBytes

		#endregion

		#region -- CompareFileTime --------------------------------------------------------

		/// <summary>Prüft, wie weit die Zeitstempel auseinander liegen.</summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static bool CompareFileTime(DateTime a, DateTime b)
		{
			return (int)((a - b).TotalSeconds) == 0;
		} // func CompareFileTime

		#endregion

		#region -- IsGZipFile, IsNoPackFile -----------------------------------------------

		public static bool IsGZipFile(string fileName)
		{
			return fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
		} // func IsGZipFile

		public static bool IsNoPackFile(string fileName)
		{
			return fileName.EndsWith(".nopack", StringComparison.OrdinalIgnoreCase);
		} // func IsGZipFile

		#endregion
	} // class Stuff
}
