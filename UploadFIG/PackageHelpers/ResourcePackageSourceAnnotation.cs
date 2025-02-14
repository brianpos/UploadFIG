using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using System.Diagnostics.CodeAnalysis;

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

	public class ResourcePackageSourceComparer : IEqualityComparer<Resource>
	{
		public bool Equals(Resource x, Resource y)
		{
			var xSource = x.Annotation<ResourcePackageSource>();
			var ySource = y.Annotation<ResourcePackageSource>();
			if (xSource != null && ySource != null)
			{
				return xSource.PackageId == ySource.PackageId
					&& xSource.PackageVersion == ySource.PackageVersion
					&& xSource.Filename == ySource.Filename;
			}
			return false;
		}

		public int GetHashCode([DisallowNull] Resource obj)
		{
			var ySource = obj.Annotation<ResourcePackageSource>();
			if (ySource != null)
			{
				return $"{ySource.PackageId}|{ySource.PackageVersion} - {ySource.Filename}".GetHashCode();
			}
			return obj.GetHashCode();
		}
	}
}