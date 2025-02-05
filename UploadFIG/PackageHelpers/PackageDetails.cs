using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UploadFIG.PackageHelpers
{
	[System.Diagnostics.DebuggerDisplay(@"Package: {packageId}|{packageVersion} Deps: {dependencies.Count} Files: {Files.Count} Resources: {resources.Count}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
	public class PackageDetails
	{
		public string packageId { get; set; }

		public string packageVersion { get; set; }

		public List<Hl7.Fhir.Model.Resource> resources { get; set; } = new ();

		public List<PackageDetails> dependencies { get; set; } = new List<PackageDetails>();

		public List<FileDetail> Files { get; set; } = new List<FileDetail>();

		public IEnumerable<CanonicalDetails> RequiresCanonicals { get; set; }

		public void DebugToConsole(string tabPrefix = "")
		{
			Console.WriteLine($"{tabPrefix}{packageId}|{packageVersion} Files: {Files.Count} Resources: {resources.Count}, Requires: {RequiresCanonicals.Count()}");
			if (dependencies.Count == 0 && RequiresCanonicals.Any())
			{
				foreach (var rc in RequiresCanonicals)
					Console.WriteLine($"{tabPrefix}    * {rc.canonical}|{rc.version}");
			}
			foreach (var dep in dependencies)
				dep.DebugToConsole(tabPrefix + "    ");
		}
	}
}
