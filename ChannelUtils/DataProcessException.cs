using System;

namespace ChannelUtils
{
	public class DataProcessException : Exception
	{
		public DataProcessException(Exception exception)
			: base(exception.Message, exception)
		{
		}
	}
}