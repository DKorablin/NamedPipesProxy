using System.Diagnostics;

namespace AlphaOmega.IO
{
	/// <summary>Provides centralized trace logging functionality for the NamedPipes infrastructure.</summary>
	public static class TraceLogic
	{
		/// <summary>The trace source used for diagnostic logging throughout the AlphaOmega.NamedPipes components.</summary>
		public static readonly TraceSource TraceSource = new TraceSource("AlphaOmega.NamedPipes");
	}
}