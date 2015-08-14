using System.Linq;
using System.Management.Automation;

namespace Neo.PowerShell.Backup
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[
	Cmdlet(VerbsCommon.Get, "backupindex"),
	OutputType(typeof(FileIndexItem))
	]
	public sealed class GetBackupIndexCmdlet : NeoCmdlet
	{
		protected override void ProcessRecord()
		{
			var index = new FileIndex();
			index.ReadIndex(Notify, Index);
			WriteObject((from c in index select FormPSObject(c, "RelativePath", "ArchiveName", "Length", "LastWriteTimeUtc")), true);
		} // proc ProcessRecord

		#region -- Arguments --------------------------------------------------------------

		[
		Parameter(Mandatory = true, Position = 0, HelpMessage = "Pfad zu der Index-Datei, die geparst werden soll."),
		Alias("path"),
		ValidateNotNullOrEmpty()
		]
		public string Index { get; set; }

		#endregion
	} // class	GetBackupIndexCmdlet
}
