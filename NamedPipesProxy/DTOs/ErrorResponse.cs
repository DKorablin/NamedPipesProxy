using System;

namespace AlphaOmega.IO.DTOs
{
	/// <summary>Represents an error response containing an error message.</summary>
	public sealed class ErrorResponse
	{
		/// <summary>Gets or sets the error message.</summary>
		public String Message { get; set; }

		/// <summary>Initializes a new instance of the <see cref="ErrorResponse"/> class with the specified error message.</summary>
		/// <param name="message">The error message.</param>
		public ErrorResponse(String message)
			=> this.Message = message;
	}
}