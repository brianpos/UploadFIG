using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;

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

		public Dictionary<string, FileDetail> CanonicalFiles { get; set; } = new Dictionary<string, FileDetail>();

		public IEnumerable<CanonicalDetails> RequiresCanonicals { get; set; } = new List<CanonicalDetails>();	

		public IEnumerable<CanonicalDetails> UnresolvedCanonicals {  get { return RequiresCanonicals.Where(c => c.resource == null).ToArray(); } }

		public void DebugToConsole(string tabPrefix = "", bool includeContent = true, bool debugRequiredByProps = false)
		{
			var unresolvedCanonicals = RequiresCanonicals.Where(c => c.resource == null).ToArray();
			Console.Write($"{tabPrefix}{packageId}|{packageVersion} \tUsing: {resources.Count()} of {Files.Count},\tRequires canonicals: {RequiresCanonicals.Count()}");
			if (unresolvedCanonicals.Length > 0)
				Console.WriteLine($" (unresolved: {unresolvedCanonicals.Length})");
			else
				Console.WriteLine();
			if (RequiresCanonicals.Any())
			{
				foreach (var rc in RequiresCanonicals.Order())
				{
					if (rc.resource == null)
						Console.WriteLine($"{tabPrefix}      - {rc.canonical}|{rc.version}");
					else if (rc.resource is IVersionableConformanceResource ivr)
					{
						var packageSource = ResourcePackageSource.PackageSourceVersion(ivr);
						Console.WriteLine($"{tabPrefix}      + {rc.canonical}|{packageSource}");
					}
					else
					{
						Console.WriteLine($"{tabPrefix}      + {rc.canonical}|{rc.version}");
					}
					if (debugRequiredByProps)
					{
						foreach (var dep in rc.requiredBy)
						{
							Console.WriteLine($"{tabPrefix}           ^- {(dep as IVersionableConformanceResource)?.Url} ({dep.TypeName}/{dep.Id})");
						}
					}
				}
				Console.WriteLine();
			}
			if (resources.Any() && includeContent)
			{
				foreach (var rc in resources.OfType<IVersionableConformanceResource>().Order(new CanonicalResourceComparer()))
				{
					Console.Write($"{tabPrefix}      {rc.Url}|{rc.Version}");
					if (rc.Status.HasValue && rc.Status == PublicationStatus.Retired)
						Console.Write($" <{rc.Status}>");
					if (rc is DomainResource dr && dr.HasAnnotation<ResourcePackageSource>())
					{
						var filename = dr.Annotation<ResourcePackageSource>().Filename;
						Console.Write($"\t({filename})");
					}
					Console.WriteLine();
				}
				Console.WriteLine();
			}
			foreach (var dep in dependencies)
				dep.DebugToConsole(tabPrefix + "    ", includeContent, debugRequiredByProps);
		}
	}
}
