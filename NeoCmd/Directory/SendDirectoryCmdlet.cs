using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Neo.PowerShell.Directory
{
	[Cmdlet(VerbsCommunications.Send, "Directory")]
	public sealed class SendDirectoryCmdlet : PSCmdlet
	{
		#region -- class SendTarget -------------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private abstract class SendTarget
		{
			private CmdletNotify notify;

			public SendTarget(CmdletNotify notify)
			{
				this.notify = notify;
			} // ctor

			public abstract void PrepareUser(string username, string password);

			public virtual Stream Create(string relativePath, out long offset)
			{
				offset = 0;
				return Create(relativePath);
			} // func Create

			public abstract Stream Create(string relativePath);

			public abstract void Delete(string relativePath);

			public CmdletNotify Notify => notify;
		} // class SendTarget

		#endregion

		#region -- class FileSendTarget ---------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class FileSendTarget : SendTarget
		{
			private DirectoryInfo target;

			public FileSendTarget(CmdletNotify notify, string target)
				: base(notify)
			{
				this.target = new DirectoryInfo(target);
			} // ctor

			public override void PrepareUser(string username, string password)
			{
			} // proc PrepareUser

			public override Stream Create(string relativePath, out long offset)
			{
				var targetFile = new FileInfo(Path.Combine(target.FullName, relativePath));

				if (targetFile.Exists) // file exists, offset content
				{
					var dst = Stuff.OpenWrite(targetFile, Notify);
					offset = Math.Max(0, dst.Length - 4096);
					dst.Position = offset;
					return dst;
				}
				else
				{
					offset = 0;
					return Stuff.OpenCreate(targetFile, Notify);
				}
			} // func Create

			public override Stream Create(string relativePath)
			{
				long offset;
				var dst = Create(relativePath, out offset);
				if (offset > 0)
					dst.Position = 0;
				return dst;
			} // func Create

			public override void Delete(string relativePath)
			{
				var targetFile = new FileInfo(Path.Combine(target.FullName, relativePath));
				if (targetFile.Exists)
					Notify.SafeIO(targetFile.Delete, $"Lösche Datei {relativePath}.");
			} // proc Delete
		} // class FileSendTarget

		#endregion

		#region -- class FtpSendTarget ----------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class FtpSendTarget : SendTarget
		{
			private Uri baseUri;
			private bool useSsl;
			private ICredentials credentials;

			private List<string> directories = new List<string>();

			public FtpSendTarget(CmdletNotify notify, string target, bool useSsl)
				: base(notify)
			{
				this.baseUri = new Uri(target, UriKind.Absolute);
				this.useSsl = useSsl;
			} // ctor

			public override void PrepareUser(string username, string password)
			{
				string domain = null;
				var split = username.IndexOf('\\');
				if (split >= 0)
				{
					domain = username.Substring(0, split);
					username = username.Substring(split + 1);
				}
				credentials = new NetworkCredential(username, password, domain);
			} // proc PrepareUser

			private FtpWebRequest FtpRequest(string path, string method)
			{
				// replace \
				path = path.Replace('\\', '/');

				// create the request
				var ftpRequest = (FtpWebRequest)WebRequest.Create(new Uri(baseUri, path));
				ftpRequest.EnableSsl = useSsl;
				ftpRequest.Credentials = credentials;
				ftpRequest.Method = method;

				return ftpRequest;
			} // func FtpWebRequest

			private bool FtpFileExists(string path)
			{
				try
				{
					var ftp = FtpRequest(path, WebRequestMethods.Ftp.GetDateTimestamp);

					using (var r = ftp.GetResponse())
					using (var src = r.GetResponseStream())
						r.Close();

					return true;
				}
				catch (WebException e)
				{
					var code = ((FtpWebResponse)e.Response).StatusCode;
					if (code == FtpStatusCode.ActionNotTakenFileUnavailableOrBusy || code == FtpStatusCode.ActionNotTakenFileUnavailable)
						return false;
					else
						throw;
				}
			} // func FtpFileExists

			private void FtpDelete(string path)
			{
				var ftp = FtpRequest(path, WebRequestMethods.Ftp.DeleteFile);
				using (var r = ftp.GetResponse())
				using (var src = ftp.GetRequestStream())
					r.Close();
			} // func FtpDelete

			private bool FtplMakeDirectory(string directoryPath)
			{
				try
				{
					bool ret = false;
					var ftp = FtpRequest(directoryPath, WebRequestMethods.Ftp.MakeDirectory);
					using (FtpWebResponse r = (FtpWebResponse)ftp.GetResponse())
					{
						ret = r.StatusCode == FtpStatusCode.PathnameCreated;
						r.Close();
					}
					return ret;
				}
				catch (WebException e)
				{
					var code = ((FtpWebResponse)e.Response).StatusCode;
					if (code == FtpStatusCode.ActionNotTakenFileUnavailableOrBusy)
						return false;
					else if (code == FtpStatusCode.ActionNotTakenFileUnavailable)
						return true;
					else
						throw;
				}
			} // func FtplMakeDirectory

			private void CheckDirectory(string directoryPath)
			{
				if (directories.Exists(c => String.Compare(c, directoryPath, StringComparison.OrdinalIgnoreCase) == 0))
					return;

				string[] parts = directoryPath.Split(new char[] { '/' });

				int j = 2;
				while (j <= parts.Length)
				{
					var tmp = String.Join("/", parts, 0, j);
	
					if (directories.Exists(c => String.Compare(c, tmp, StringComparison.OrdinalIgnoreCase) == 0))
						j++;
          else if (FtplMakeDirectory(tmp))
					{
						directories.Add(tmp);
						j++;
					}
					else
						break;
				}
			} // proc CheckDirectory

			public override Stream Create(string relativePath)
			{
				if (FtpFileExists(relativePath))
					FtpDelete(relativePath);

				var uri = new Uri(baseUri, relativePath);
				var pos = uri.AbsolutePath.LastIndexOf('/');
				if (pos > 0)
					CheckDirectory(uri.AbsolutePath.Substring(0, pos));

				var ftp = FtpRequest(relativePath, WebRequestMethods.Ftp.UploadFile);
				return ftp.GetRequestStream();
			} // func Create

			public override void Delete(string relativePath)
			{
				if (FtpFileExists(relativePath))
					FtpDelete(relativePath);
			} // proc Delete
		} // class FtpSendTarget

		#endregion

		protected override void ProcessRecord()
		{
			var notify = new CmdletNotify(this);

			using (var bar = notify.CreateStatus("Übertrage Dateien", $"Übertrage nach {Target}..."))
			{
				// create target provider
				SendTarget target;
				if (Target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
					target = new FtpSendTarget(notify, Target, false);
				else if (Target.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
					target = new FtpSendTarget(notify, "ftp" + Target.Substring(4), true);
				else
					target = new FileSendTarget(notify, Target);

				// add credentials
				if (!String.IsNullOrEmpty(Username))
					target.PrepareUser(Username, Password);

				// collect alle file to send
				var totalSize = 0L;
				var files = new List<FileListItem>();
				var indexFile = (FileListItem)null;
				var indexRemoveFile = (FileListItem)null;
				var basePath = new DirectoryInfo(Source);
				foreach (var currentItem in new FileList(notify, basePath, Excludes))
				{
					if (currentItem.RelativePath == "index.txt.gz") // search for index file, to upload it as the last file
					{
						indexFile = currentItem;
						totalSize += currentItem.FileInfo.Length;
					}
					else if (currentItem.RelativePath == "index_rm.txt") // and search for the remove file
					{
						indexRemoveFile = currentItem;
					}
					else
					{
						files.Add(currentItem);
						totalSize += currentItem.FileInfo.Length;
					}
				}
				files.Add(indexFile);

				// Upload files
				if (files.Count > 0)
				{
					bar.Maximum = totalSize;
					bar.StartRemaining();
					foreach (var currentItem in files)
					{
						// upload file
						using (var dst = target.Create(currentItem.RelativePath, out var offset))
						using (var src = currentItem.FileInfo.OpenRead(notify))
						{
							src.Position = offset;
							Stuff.CopyRawBytes(bar, currentItem.RelativePath, currentItem.FileInfo.Length, src, dst);
						}

						// remove uploaded files
						if (RemoveSyncedFiles)
							notify.SafeIO(currentItem.FileInfo.Delete, $"Lösche Datei {currentItem.RelativePath}.");
					}
					bar.StopRemaining();
				}

				// remove files
				if (indexRemoveFile != null)
				{
					using (var sr = new StreamReader(Stuff.OpenRead(indexRemoveFile.FileInfo, notify)))
					{
						var file = sr.ReadLine();
						while (file != null)
						{
							target.Delete(file);
							file = sr.ReadLine();
						}
					}

					notify.SafeIO(indexRemoveFile.FileInfo.Delete, $"Datei {indexRemoveFile.RelativePath} konnte nicht gelöscht werden.");
				}
			}
		} // proc ProcessRecord

		[
		Parameter(Mandatory = true, Position = 0, HelpMessage = "Verzeichnis, welches synchronisiert werden soll."),
		Alias("path")
		]
		public string Source { get; set; }
		[
		Parameter(Mandatory = true, Position = 1, HelpMessage = "Zielort für die Dateien."),
		Alias("dest")
		]
		public string Target { get; set; }
		[
		Parameter(Mandatory = false, HelpMessage = "Nutzer für uploads auf ftp-Server"),
		Alias("user")
		]
		public string Username { get; set; }
		[
		Parameter(Mandatory = false, HelpMessage = "Passwort für uploads auf ftp-Server"),
		Alias("pass")
		]
		public string Password { get; set; }

		[
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien die ausgeschlossen werden sollen."),
		Alias("filter")
		]
		public string[] Excludes { get; set; }
		[
			Parameter(Mandatory = false, HelpMessage = "Sollen die Dateien nach erfolgreichen Upload gelöscht werden.")
		]
		public bool RemoveSyncedFiles { get; set; } = false;

		static SendDirectoryCmdlet()
		{
			ServicePointManager.ServerCertificateValidationCallback = (
				 Object sender,
				 X509Certificate certificate,
				 X509Chain chain,
				 SslPolicyErrors sslPolicyErrors) => true;
		} // sctor
	} // class SendDirectoryCmdlet
}