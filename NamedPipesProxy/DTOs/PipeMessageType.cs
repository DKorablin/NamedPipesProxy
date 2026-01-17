namespace AlphaOmega.IO.DTOs
{
	/// <summary>Defines the types of messages that can be transmitted through named pipes.</summary>
	public enum PipeMessageType
	{
		/// <summary>Represents an empty or void message with no payload.</summary>
		Void,
		/// <summary>Represents a null or empty response message.</summary>
		Null,
		/// <summary>Represents an error message containing error information.</summary>
		Error,
		/// <summary>Represents a worker registration request message.</summary>
		RegisterWorker,
	}
}