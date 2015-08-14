using System;
using System.IO;
using System.Management.Automation;

namespace Neo.PowerShell.Directory
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[
	Cmdlet(VerbsCommon.Get, "directorylist")
  OutputType(new Type[] { typeof(FileListItem) })
	]
	public sealed class GetDirectoryListCmdlet : NeoCmdlet
	{
		protected override void ProcessRecord()
		{
			var files = new FileList(Notify, new DirectoryInfo(Path), Excludes);
      WriteObject(files);
		} // proc ProcessRecord

		[
		Parameter(Mandatory = true, Position = 0, HelpMessage = "Verzeichnis, welches durchsucht werden soll.")
		]
		public string Path { get; set; }

		[
		Parameter(Mandatory = false, HelpMessage = "Filter für Dateien die ausgeschlossen werden sollen.")
		]
		public string[] Excludes { get; set; }
	} // class GetDirectoryListCmdlet
}
