using Hl7.Fhir.Model;
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

		public IEnumerable<Hl7.Fhir.Model.Resource> resources { get
			{
				return Files.Select(f => f.resource).Where(r => r != null);
			}
		}

		public List<PackageDetails> dependencies { get; set; } = new List<PackageDetails>();

		public List<FileDetail> Files { get; set; } = new List<FileDetail>();

		public IEnumerable<CanonicalDetails> RequiresCanonicals { get; set; }

		public IEnumerable<CanonicalDetails> UnresolvedCanonicals {  get { return RequiresCanonicals.Where(c => c.resource == null).ToArray(); } }

		public void DebugToConsole(string tabPrefix = "", bool debugRequiredByProps = false)
		{
			var unresolvedCanonicals = RequiresCanonicals.Where(c => c.resource == null).ToArray();
			Console.Write($"{tabPrefix}{packageId}|{packageVersion} \tUsing: {resources.Count()} of {Files.Count},\tRequires canonicals: {RequiresCanonicals.Count()}");
			if (unresolvedCanonicals.Length > 0)
				Console.WriteLine($" (unresolved: {unresolvedCanonicals.Length})");
			else
				Console.WriteLine();
			if (unresolvedCanonicals.Length > 0 && RequiresCanonicals.Any())
			{
				foreach (var rc in unresolvedCanonicals.Order())
				{
					Console.WriteLine($"{tabPrefix}      * {rc.canonical}|{rc.version}");
					if (debugRequiredByProps)
					{
						foreach (var dep in rc.requiredBy)
						{
							Console.WriteLine($"{tabPrefix}           - {(dep as IVersionableConformanceResource)?.Url} ({dep.TypeName}/{dep.Id})");
						}
					}
				}
				Console.WriteLine();
			}
			foreach (var dep in dependencies)
				dep.DebugToConsole(tabPrefix + "    ");
		}
	}
}
