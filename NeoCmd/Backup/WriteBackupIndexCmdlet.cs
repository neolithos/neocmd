using System.Collections.Generic;
using System.Management.Automation;

namespace Neo.PowerShell.Backup
{
	[
	Cmdlet(VerbsData.Save, "BackupIndex"),
	OutputType(typeof(FileIndexItem))
	]
	public sealed class WriteBackupIndexCmdlet : NeoCmdlet
	{
		protected override void ProcessRecord()
		{
			var index = new FileIndex();

			foreach (var item in Items)
				index.UpdateFile(item);

			index.WriteIndex(Notify, Index);
		} // proc ProcessRecord

		[
		Parameter(Mandatory = true, Position = 1, HelpMessage = "Datei, in welche die Index-Einträge geschrieben werden sollen."),
		Alias("path")
		]
		public string Index { get; set; }
		[
		Parameter(ValueFromPipeline = true, HelpMessage = "Einträge die geschrieben werden sollen")
		]
		public IEnumerable<FileListItem> Items { get; set; }
	} // class WriteBackupIndexCmdlet
}
