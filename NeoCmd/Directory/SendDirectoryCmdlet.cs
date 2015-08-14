using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public sealed class SendDirectoryCmdlet
	{
		//#region -- class SyncDirectory ------------------------------------------------------

		/////////////////////////////////////////////////////////////////////////////////
		///// <summary></summary>
		//[Cmdlet(VerbsCommunications.Send, "directory")]
		//public class SendDirectory : PSCmdlet
		//{
		//	protected override void ProcessRecord()
		//	{
		//		ServicePointManager.ServerCertificateValidationCallback = (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors error) => true;

		//		var notify = new CmdletNotify(this);
		//		var basePath = new DirectoryInfo(Source);

		//		// create upload list
		//		var totalSize = 0L;
		//		var useFtp = false;
		//		var useSsl = false;
		//		NetworkCredential credentials = null;

		//		if (Target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
		//			useFtp = true;
		//		else if (Target.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
		//     {
		//			useFtp = true;
		//			useSsl = true;
		//			Target = "ftp" + Target.Substring(4);
		//		}

		//		if (!String.IsNullOrEmpty(Username))
		//			credentials = new NetworkCredential(Username, Password);

		//		var files = new List<FileListItem>();
		//		var targetFileList = new List<string>();
		//		using (var bar = notify.CreateStatus("Bereite hochladen vor", "Dateien hochloaden..."))
		//		{
		//			foreach (var currentItem in new FileList(notify, basePath, Excludes))
		//			{
		//				files.Add(currentItem);
		//				totalSize += currentItem.FileInfo.Length;
		//			}

		//			var request = PrepareFtpRequest(String.Empty, useSsl, credentials);
		//			request.Method = WebRequestMethods.Ftp.ListDirectory;

		//			using (var response = request.GetResponse())
		//			using (var tr = new StreamReader(response.GetResponseStream(), Encoding.Default, true, 4096, false))
		//			{
		//				string line;
		//				while ((line = tr.ReadLine()) != null)
		//					targetFileList.Add(line);
		//			}
		//		}

		//		using (var bar = notify.CreateStatus("Lade Dateien hoch.", "Dateien werden hochgeladen..."))
		//		{
		//			bar.Maximum = totalSize;
		//			foreach (var currentItem in files)
		//			{
		//				if (useFtp)
		//				{
		//					// Existiert die Datei auf dem Server
		//					if (targetFileList.Exists(c => String.Compare(c, currentItem.RelativePath, StringComparison.OrdinalIgnoreCase) == 0))
		//					{
		//						var request = PrepareFtpRequest(currentItem.RelativePath, useSsl, credentials);
		//						request.Method = WebRequestMethods.Ftp.DeleteFile;
		//						using (var response = request.GetResponse())
		//						{ }
		//					}

		//					// Hochladen der Datei
		//					{
		//						var request=PrepareFtpRequest(currentItem.RelativePath, useSsl, credentials);
		//						request.Method = WebRequestMethods.Ftp.UploadFile;
		//						using (var src = currentItem.FileInfo.OpenRead(notify))
		//						using (var dst = request.GetRequestStream())
		//							Stuff.CopyRawBytes(bar, currentItem.RelativePath, currentItem.FileInfo.Length, src, dst);
		//						using (var response = request.GetResponse())
		//						{ }
		//					}
		//				}
		//				else // Dateiupload auf UNC oder Pfad
		//				{
		//					using (var dst = Stuff.OpenCreate(new FileInfo(Path.Combine(Target, currentItem.RelativePath)), notify))
		//					using (var src = currentItem.FileInfo.OpenRead(notify))
		//						Stuff.CopyRawBytes(bar, currentItem.RelativePath, currentItem.FileInfo.Length, src, dst);
		//				}

		//				if (RemoveSyncedFiles)
		//					notify.SafeIO(currentItem.FileInfo.Delete, $"Lösche Datei {currentItem.RelativePath}.");
		//			}
		//		}
		//	} // proc ProcessRecord

		//	private FtpWebRequest PrepareFtpRequest(string name, bool useSsl, NetworkCredential credentials)
		//	{
		//		var ftpRequest = (FtpWebRequest)WebRequest.Create(new Uri(new Uri(Target), name));
		//		ftpRequest.EnableSsl = useSsl;
		//		ftpRequest.Credentials = credentials;
		//		return ftpRequest;
		//	} // func FtpWebRequest

		//	[
		//	Parameter(Mandatory = true, Position = 0, HelpMessage = "Verzeichnis, welches synchronisiert werden soll."),
		//	Alias("path")
		//	]
		//	public string Source { get; set; }
		//	[
		//	Parameter(Mandatory = true, Position = 1, HelpMessage = "Zielort für die Dateien."),
		//	Alias("dest")
		//	]
		//	public string Target { get; set; }
		//	[
		//	Parameter(Mandatory = false, HelpMessage = "Nutzer für uploads auf ftp-Server"),
		//	Alias("user")
		//	]
		//	public string Username { get; set; }
		//	[
		//	Parameter(Mandatory = false, HelpMessage = "Passwort für uploads auf ftp-Server"),
		//	Alias("pass")
		//	]
		//	public string Password { get; set; }

		//	[
		//	Parameter(Mandatory = false, HelpMessage = "Filter für Dateien die ausgeschlossen werden sollen."),
		//	Alias("filter")
		//	]
		//	public string[] Excludes { get; set; }
		//	[
		//		Parameter(Mandatory = false, HelpMessage = "Sollen die Dateien nach erfolgreichen Upload gelöscht werden.")
		//	]
		//	public bool RemoveSyncedFiles { get; set; } = false;
		//} // class SyncDirectory

		//#endregion
	} // class SendDirectoryCmdlet
}