using Hl7.Fhir.Model;

namespace UploadFIG
{
	public static class ConsoleEx
	{
		public static void WriteLine(ConsoleColor color, string message)
		{
			var oldColor = Console.ForegroundColor;
			if (color != oldColor)
				Console.ForegroundColor = color;
			Console.WriteLine(message);
			if (color != oldColor)
				Console.ForegroundColor = oldColor;
		}

		public static void Write(ConsoleColor color, string message)
		{
			var oldColor = Console.ForegroundColor;
			if (color != oldColor)
				Console.ForegroundColor = color;
			Console.Write(message);
			if (color != oldColor)
				Console.ForegroundColor = oldColor;
		}
	}
}
