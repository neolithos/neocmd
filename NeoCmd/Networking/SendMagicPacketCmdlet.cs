using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Neo.PowerShell.Networking
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Cmdlet(VerbsCommunications.Send, "magicpacket", DefaultParameterSetName = "MacAddress")]
	public sealed class SendMagicPacketCmdlet : Cmdlet
	{
		private byte[] macAddress = new byte[6];
		private byte[] packetData = new byte[102];
		private EndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, 12287);

		private Socket sendSocet;

		#region -- ProcessRecord ----------------------------------------------------------

		protected override void BeginProcessing()
		{
			base.BeginProcessing();

			sendSocet = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			sendSocet.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
		} // proc BeginProcessing

		protected override void ProcessRecord()
		{
			// Signatur
			for (int i = 0; i < 6; i++)
				packetData[i] = 255;

			// 6x die Mac
			for (int j = 0; j < 16; j++)
				macAddress.CopyTo(packetData, (j + 1) * 6);

			// Daten senden
			if (sendSocet.SendTo(packetData, broadcastEndPoint) != packetData.Length)
				throw new ArgumentException();
		} // proc ProcessRecord

		protected override void EndProcessing()
		{
			if (sendSocet != null)
			{
				sendSocet.Close();
				sendSocet = null;
			}
			base.EndProcessing();
		} // proc EndProcessing

		#endregion

		#region -- Arguments --------------------------------------------------------------

		private void ClearMacAddress()
		{
			for (int i = 0; i < 6; i++)
				macAddress[i] = 0;
		} // proc ClearMacAddress

		private void SetMacAddress(string value)
		{
			var i = 0;
			var j = 0;
			var state = 0;
			var segDone = false;

			// Lösche Inhalt
			ClearMacAddress();
			value = value.Trim();
			while (i < value.Length)
			{
				char c = char.ToUpper(value[i]);
				if (":-.".IndexOf(c) != -1)
				{
					state = 0;
					segDone = true;
					i++;
				}
				else
				{
					var n = "0123456789ABCDEF".IndexOf(c);
					if (n == -1)
						throw new FormatException("Hexadezimalzahl erwartet.");

					switch (state)
					{
						case 0:
							if (segDone)
							{
								j++;
								if (j >= 6)
									throw new FormatException("Mac zu lang.");
							}

							macAddress[j] = (byte)n;
							state = 1;
							segDone = false;
							break;
						case 1:
							macAddress[j] = (byte)((int)macAddress[j] << 4 | n);
							state = 0;
							segDone = true;
							break;
					}
					i++;
				}
			}
		} // proc SetMacAddress

		[
		Parameter(Position = 0, Mandatory = true, ParameterSetName = "MacAddress", HelpMessage = "Mac-Adresse", ValueFromPipeline = true), 
		ValidateNotNullOrEmpty()
		]
		public string MacAddress
		{
			get
			{
				return string.Format("{0:X2}-{1:X2}-{2:X2}-{3:X2}-{4:X2}-{5:X2}", new object[] { macAddress[0], macAddress[1], macAddress[2], macAddress[3], macAddress[4], macAddress[5] });
			}
			set
			{
				if (string.IsNullOrEmpty(value))
				{
					this.ClearMacAddress();
					return;
				}
				this.SetMacAddress(value);
			}
		} // proc MacAddress

		#endregion
	} // class SendMagicPacketCmdlet
}
