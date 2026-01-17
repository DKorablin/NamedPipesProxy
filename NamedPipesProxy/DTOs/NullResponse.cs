namespace AlphaOmega.IO.DTOs
{
	/// <summary>Represents a response that indicates success without carrying any data.</summary>
	/// <remarks>This class follows the singleton pattern to ensure only one instance exists.</remarks>
	public sealed class NullResponse
	{
		/// <summary>Gets the singleton instance of the <see cref="NullResponse"/> class.</summary>
		/// <remarks>Use this instance instead of creating new instances of this class.</remarks>
		public static readonly NullResponse Instance = new NullResponse();

		/// <summary>Prevents a default instance of the <see cref="NullResponse"/> class from being created.</summary>
		private NullResponse()
		{
		}
	}
}