using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;
using System.Diagnostics.CodeAnalysis;

namespace UploadFIG
{
	/// <summary>
	/// Sorts Canonical resources
	/// </summary>
	public static class CurrentCanonicalFromPackages
	{
		static public IVersionableConformanceResource Current(IEnumerable<IVersionableConformanceResource> list)
		{
			var ordered = list.PackageOrdered();
			return ordered.FirstOrDefault();
		}

		static public IEnumerable<IVersionableConformanceResource> PackageOrdered(this IEnumerable<IVersionableConformanceResource> list)
		{
			var comparer = new CurrentCanonicalComparer(list);
			var psComparer = new ResourcePackageSourceComparer();
			IEnumerable<IVersionableConformanceResource> result = list.OrderBy(CurrentCanonical.StatusPrecedence)
				.ThenByDescending((f) => f, comparer)
				.ThenByDescending((f) => f, psComparer)
				.ThenBy(CurrentCanonical.ResourceIdOrder);
			return result;
		}
	}
}