using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Neo.PowerShell
{
	[RunInstaller(true)]
	public class NeoCmdSnapIn : CustomPSSnapIn
	{
		public NeoCmdSnapIn()
		{
			foreach (var type in typeof(NeoCmdSnapIn).Assembly.GetTypes())
			{
				var attr = type.GetCustomAttribute<CmdletAttribute>();
				if (attr != null)
					Cmdlets.Add(new CmdletConfigurationEntry(attr.VerbName + "-" + attr.NounName, type, attr.HelpUri));
			}
		} // ctor

		public override string Name => "NeoCmd";
		public override string Description => "Befehle von Neolithos.";
		public override string Vendor => "Pefrect Working (Landhai)";
	} // class NeoCmdSnapIn
}