using System;
using System.Net;
using System.Net.Sockets;

namespace Demo
{
	internal static class RoleDetector
	{
		public static Boolean TryBecomeRegistry(out TcpListener listener)
		{
			try
			{
				listener = new TcpListener(IPAddress.Loopback, 8080);
				listener.Start();
				return true;
			} catch(SocketException exc)
			{
				Console.WriteLine($"Could not start registry listener: {exc.Message}");
				listener = null;
				return false;
			}
		}
	}
}