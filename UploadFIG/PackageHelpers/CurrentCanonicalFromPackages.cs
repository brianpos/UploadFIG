using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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

        static public FileDetail Current(IEnumerable<FileDetail> list)
        {
            var ordered = list.PackageOrdered();
            return ordered.FirstOrDefault();
        }

        static public IEnumerable<FileDetail> PackageOrdered(this IEnumerable<FileDetail> list)
        {
            var comparer = new CurrentCanonicalComparer(list.Select(f => f.resource as IVersionableConformanceResource));
            var psComparer = new ResourcePackageSourceComparer();
            IEnumerable<FileDetail> result = list.OrderBy(StatusPrecedence)
                .ThenByDescending((f) => f.resource as IVersionableConformanceResource, comparer)
                .ThenByDescending((f) => f.resource as IVersionableConformanceResource, psComparer)
                .ThenBy((f) => CurrentCanonical.ResourceIdOrder(f.resource as IVersionableConformanceResource));
            return result;

        }

        public static Func<FileDetail, int> StatusPrecedence = (f) => CurrentCanonical.StatusPrecedence(f.resource as IVersionableConformanceResource);
    }
}
