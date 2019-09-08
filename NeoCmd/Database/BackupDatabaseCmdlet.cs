using System;
using System.Data.SqlClient;
using System.Linq;
using System.Management.Automation;

namespace Neo.PowerShell.Database
{
	[Cmdlet(VerbsCommon.New, "BackupDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
	public class BackupDatabaseCmdlet : NeoCmdlet
	{
		private CmdletProgress progress = null;

		protected override void ProcessRecord()
		{
			using (var con = new SqlConnection(Connection))
			{
				con.Open();

				con.InfoMessage += Con_InfoMessage;
				
				using (var bar = Notify.CreateStatus("Backup database", $"Backup Database {con.Database}..."))
				{
					progress = bar;

					// get index information
					using (var cmd = con.CreateCommand())
					{
						bar.ActivityText = $"Check indizes {con.Database}...";
						cmd.CommandText = String.Format(String.Join(Environment.NewLine,
							"SELECT s.[name], o.[name], i.[name], avg_fragmentation_in_percent, fragment_count",
							"	FROM sys.dm_db_index_physical_stats(DB_ID(N'{0}'), NULL, NULL, NULL, NULL) AS f",
							"	INNER JOIN sys.indexes AS i ON(f.object_id = i.object_id AND f.index_id = i.index_id)",
							"	INNER JOIN sys.objects AS o ON(i.object_id = o.object_id)",
							"	INNER JOIN sys.schemas s on(o.schema_id = s.schema_id)"), con.Database
						);

						using (var r = cmd.ExecuteReader())
						{
							while (r.Read())
							{
								var schemaName = r.GetString(0);
								var tableName = r.GetString(1);
								var indexName = r.GetString(2);

								var frag = r.GetDouble(3);
								var fragCount = r.IsDBNull(4) ? 0 : r.GetInt64(4);

								var action =
									frag >= 5.0f && frag < 30.0
										? "REORGANIZE"
										: frag >= 30.0f
											? "REBUILD"
											: null;
								if (action != null)
								{
									using (var cmd2 = con.CreateCommand())
									{
										bar.StatusDescription = $"Index {indexName} of {schemaName}.{tableName} - {action} (fragmentation: {frag:N1})...";
										cmd2.CommandText = $"ALTER INDEX [{indexName}] ON [{schemaName}].[{tableName}] {action}";
										cmd2.ExecuteNonQuery();
									}
								}
							}
						}
					}

					using (var cmd = con.CreateCommand())
					{
						bar.ActivityText = $"Execute Backup for {con.Database}...";
						cmd.CommandText = String.Format(String.Join(Environment.NewLine,
								"BACKUP DATABASE [{0}]",
								"TO DISK = N'{1}' WITH NOFORMAT, INIT, ",
								"NAME = N'{0}-Vollständig Datenbank Sichern', SKIP, NOREWIND, NOUNLOAD, COMPRESSION, ",
								"STATS = 1, CHECKSUM"
							),
							con.Database,
							BackupFile
						);
						// do backup
						bar.Maximum = 100;
						bar.StartRemaining();
						cmd.ExecuteScalar(); // ExecuteNonQuery does not fire InfoMessage
						bar.StopRemaining();

						// check backup state
						bar.ActivityText = $"Check Backup for {con.Database}...";
						cmd.CommandText = String.Format("select position from msdb..backupset where database_name = N'{0}' and backup_set_id = (select max(backup_set_id) from msdb..backupset where database_name = N'{0}')", con.Database);
						var pos = cmd.ExecuteScalar();
						if (pos is DBNull)
							throw new Exception("Backup information not found.");

						bar.ActivityText = $"Verify Backup for {con.Database}...";
						cmd.CommandText= String.Format("RESTORE VERIFYONLY FROM DISK = N'{0}' WITH FILE = {1}, NOUNLOAD, NOREWIND", BackupFile, pos);
						cmd.ExecuteScalar();
					}
	
				}
			}
		} // proc ProcessRecord

		private void Con_InfoMessage(object sender, SqlInfoMessageEventArgs e)
		{
			if (progress == null)
				return;

			foreach(var m in e.Errors.OfType<SqlError>())
			{
				var idx = m.Message.IndexOf(' ');
				if (m.Number == 3211 && idx > 0 && Int32.TryParse(m.Message.Substring(0, idx), out var percent))
					progress.Position = percent;
				else
					WriteObject(m.Message);
			}
		} // event Con_InfoMessage

		#region -- Arguments ----------------------------------------------------------

		[
		Parameter(Position = 0, Mandatory = true),
		Alias("con"),
		ValidateNotNullOrEmpty
		]
		public string Connection { get; set; }

		[
		Parameter(Position = 1, Mandatory = true),
		Alias("destination", "dest"),
		ValidateNotNullOrEmpty
		]
		public string BackupFile { get; set; }

		#endregion
	} // class BackupDatabaseCmdlet
}
