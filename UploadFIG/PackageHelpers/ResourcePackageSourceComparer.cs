using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.FhirPath.Sprache;
using System.Diagnostics.CodeAnalysis;

namespace UploadFIG
{
	/// <summary>
	/// This will remove any resources that were loaded from the same package in other dependency chains.
	/// (It does not consider canonicals at all)
	/// </summary>
	public class ResourcePackageSourceComparer : IEqualityComparer<Resource>, IComparer<IVersionableConformanceResource>
	{
		public int Compare(IVersionableConformanceResource x, IVersionableConformanceResource y)
		{
			var xSource = (x as Resource)?.Annotation<ResourcePackageSource>();
			var ySource = (y as Resource)?.Annotation<ResourcePackageSource>();
			if (xSource != null && ySource != null)
			{
				var result = string.CompareOrdinal(xSource.PackageId, ySource.PackageId);
				if (result != 0)
					return result;

				result = string.CompareOrdinal(xSource.PackageVersion, ySource.PackageVersion);
				if (result != 0)
					return result;

				result = string.CompareOrdinal(xSource.Filename, ySource.Filename);
				return result;
			}
			return String.Compare((x as Resource)?.Id, (y as Resource)?.Id);
		}

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