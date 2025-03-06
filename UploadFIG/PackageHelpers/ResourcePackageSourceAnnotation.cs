using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;

namespace UploadFIG
{
	/// <summary>
	/// Resource Annotation for the name of the example file in the package
	/// </summary>
	public record ResourcePackageSource
	{
		public string PackageId { get; init; }
		public string PackageVersion { get; init; }
		public string Filename { get; init; }

		public static string PackageSourceVersion(IVersionableConformanceResource resource)
		{
			var att = (resource as Resource).Annotation<ResourcePackageSource>();
			if (att != null)
			{
				var result = $"{resource.Version} ({att.PackageId}|{att.PackageVersion}";
				if (resource.Status == PublicationStatus.Retired)
					result += " <retired>";
				result += ")";
				return result;
			}
			return null;
		}
	}
}