using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Neo.PowerShell
{
	#region -- class CmdletProgress -----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Verwaltet den Fortschritt einer Operation</summary>
	public sealed class CmdletProgress : IDisposable
	{
		private readonly int sourceId;
		private readonly PSHostUserInterface ui;
		private readonly ProgressRecord progress;

		private long position = 0L;
		private long maximum = 100L;
		private int lastPercent = -1;
		private int lastSeconds = -1;
		private int lastSecondsUpdate = -1;

		private Stopwatch updateTime = null;

		/// <summary></summary>
		/// <param name="ui"></param>
		/// <param name="activity"></param>
		/// <param name="text"></param>
		public CmdletProgress(PSHostUserInterface ui, string activity, string text)
		{
			this.sourceId = Environment.TickCount;
			this.ui = ui;
			this.progress = new ProgressRecord(Math.Abs(activity.GetHashCode()), activity, text);

			ui.WriteProgress(sourceId, progress);
		} // ctor

		public void Dispose()
		{
			progress.RecordType = ProgressRecordType.Completed;
			ui.WriteProgress(sourceId, progress);
		} // proc Dispose

		private void UpdatePercent(long position, long maximum)
		{
			var updateUI = false;

			// Prüfe die Prozentanzeige
			var tmp = unchecked((int)(position * 100 / maximum));
			if (tmp != lastPercent)
			{
				lastPercent = tmp;
				updateUI = true;
			}

			// Restzeit Ermittlung
			if (updateTime != null && position > 0 && (lastSecondsUpdate == -1 || Math.Abs(Environment.TickCount - lastSecondsUpdate) > 1000))
			{
				var tmp1 = unchecked((int)((maximum - position) * updateTime.ElapsedMilliseconds / position / 1000));
				if (tmp1 != lastSeconds)
				{
					lastSeconds = tmp1;
					updateUI = true;
				}
				lastSecondsUpdate = Environment.TickCount;
			}

			// Schreibe die Informationen
			if (updateUI)
				UpdateProgressBar();
		} // proc UpdatePercent

		private void UpdateProgressBar()
		{
			progress.SecondsRemaining = lastSeconds;
			progress.PercentComplete = lastPercent;
			ui.WriteProgress(sourceId, progress);
		} // proc UpdateProgressBar

		public void StartRemaining()
		{
			updateTime = Stopwatch.StartNew();
		} // proc StartRemaining

		public void StopRemaining()
		{
			lastSeconds = -1;
			UpdateProgressBar();
		} // proc StopRemaining

		public long Position { get { return position; } set { UpdatePercent(position = value, maximum); } }
		public long Maximum { get { return maximum; } set { UpdatePercent(position, maximum = value); } }

		public string StatusText
		{
			get { return progress.CurrentOperation; }
			set
			{
				progress.CurrentOperation = value;
				ui.WriteProgress(sourceId, progress);
			}
		} // prop StatusText
	} // class CmdletProgress

	#endregion

	#region -- class CmdletNotify -------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Helper für die Ausgabe von Daten in den Host.</summary>
	public sealed class CmdletNotify
	{
		private PSCmdlet cmdlet;

		public CmdletNotify(PSCmdlet cmdlet)
		{
			this.cmdlet = cmdlet;
		} // ctor

		/// <summary>Führt eine IO-Operation in einen sicheren Kontext aus.</summary>
		/// <param name="action">Action</param>
		/// <param name="actionDescription">Beschreibung der Operation.</param>
		public void SafeIO(Action action, string actionDescription)
		{
			const int max = 6;
			var tries = max;
			while (tries > 0)
				try
				{
					action();
					return;
				}
				catch (UnauthorizedAccessException)
				{
					Thread.Sleep(1000 * max - tries + 1);
					tries--;
				}
				catch (IOException e)
				{
					if (!PromptChoice(actionDescription, e))
					{
						Thread.Sleep(1000 * max - tries + 1);
						tries--;
					}
				}
		} // proc SafeIO

		private bool PromptChoice(string actionDescription, IOException e)
		{
			var choices = new Collection<ChoiceDescription>();
			choices.Add(new ChoiceDescription("&Wiederholen"));
			choices.Add(new ChoiceDescription("&Abbrechen"));
			try
			{
				if (UI.PromptForChoice("Operation fehlgeschlagen", $"{actionDescription}\n{e.Message}\n\nVorgang wiederholen?", choices, 0) == 1)
					Abort();

				return true;
			}
			catch (NotImplementedException) { return false; }
			catch (NotSupportedException) { return false; }
		} // func PromptChoice

		/// <summary>Bricht die Pipeline ab.</summary>
		public void Abort()
		{
			cmdlet.ThrowTerminatingError(new ErrorRecord(new PipelineStoppedException(), "Canceled", ErrorCategory.OperationStopped, null));
		} // proc Abort

		/// <summary>Führt eine IO-Operation in einen sicheren Kontext aus.</summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="func"></param>
		/// <param name="actionDescription"></param>
		/// <returns></returns>
		public T SafeIO<T>(Func<T> func, string actionDescription)
		{
			object o = null;
			SafeIO(() => { o = func(); }, actionDescription);
			return o == null ? default(T) : (T)o;
		} // proc SafeIO

		public Stream SafeOpen(Func<FileInfo, Stream> func, FileInfo file, string actionDescription)
		{
			const int max = 6;
			var tries = max;
			while (true)
				try
				{
					return func(file);
				}
				catch (IOException e)
				{
					var choices = new Collection<ChoiceDescription>();
					choices.Add(new ChoiceDescription("&Wiederholen"));
					choices.Add(new ChoiceDescription("&Überspringen"));
					choices.Add(new ChoiceDescription("&Abbrechen"));
					try
					{
						switch (UI.PromptForChoice("Operation fehlgeschlagen", $"{actionDescription}\n{e.Message}\n\nVorgang wiederholen?", choices, 0))
						{
							case 1:
								return new MemoryStream(0);
							case 2:
								Abort();
								break;
						}
					}
					catch (Exception ex) when (ex is NotSupportedException || ex is NotImplementedException)
					{
						if (tries > 0)
						{
							Thread.Sleep(1000 * max - tries + 1);
							tries--;
						}
						else
							return new MemoryStream(0);
					}
				}
		} // func SafeOpen

		/// <summary>Erzeugt einen Status.</summary>
		/// <param name="activity">Überschrift für die Operation</param>
		/// <param name="text">Beschreibender Langtext der Operation.</param>
		/// <returns></returns>
		public CmdletProgress CreateStatus(string activity, string text) => new CmdletProgress(UI, activity, String.IsNullOrEmpty(text) ? activity : text);

		public PSHostUserInterface UI => cmdlet.Host.UI;
	} // class CmdletNotify

	#endregion

	#region -- class NeoCmdlet ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class NeoCmdlet : PSCmdlet
	{
		private CmdletNotify notify = null;

		protected override void BeginProcessing()
		{
			base.BeginProcessing();
			notify = new CmdletNotify(this);
		} // proc BeginProcessing

		public CmdletNotify Notify => notify;

		// -- Static ----------------------------------------------------------------------

		public static PSObject FormPSObject(object obj, params string[] defaultProperties)
		{
			var p = new PSObject(obj);
			p.Members.Add(
				new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
					{
						new PSPropertySet("DefaultDisplayPropertySet", defaultProperties)
					}
				)
			);	

			return p;
		} // func FormPSObject

	} // class NeoCmdlet

	#endregion
}
