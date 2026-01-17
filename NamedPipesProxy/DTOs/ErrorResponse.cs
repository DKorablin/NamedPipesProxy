using System;

namespace AlphaOmega.IO.DTOs
{
	public sealed class ErrorResponse
	{
		public String Message { get; set; }

		public ErrorResponse(String message)
			=> this.Message = message;
	}
}