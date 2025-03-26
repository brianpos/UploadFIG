using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using System.Diagnostics.CodeAnalysis;

namespace UploadFIG
{
    /// <summary>
    /// Sorts Canonical resources
    /// </summary>
    public class CanonicalResourceComparer : IEqualityComparer<IVersionableConformanceResource>, IComparer<IVersionableConformanceResource>
    {
        public int Compare(IVersionableConformanceResource x, IVersionableConformanceResource y)
        {
            // Compare the canonical URLs
            int result = string.CompareOrdinal(x.Url, y.Url);
            if (result != 0)
                return result;

            // If the URLs are the same, compare the versions
            result = string.CompareOrdinal(x.Version, y.Version);
            if (result != 0)
                return result;

            // ComparePackage Sources
            var xSource = (x as Resource).Annotation<ResourcePackageSource>();
            var ySource = (y as Resource).Annotation<ResourcePackageSource>();
            if (xSource != null && ySource != null)
            {
                result = string.CompareOrdinal(xSource.PackageId, ySource.PackageId);
                if (result != 0)
                    return result;

                result = string.CompareOrdinal(xSource.PackageVersion, ySource.PackageVersion);
                if (result != 0)
                    return result;

                result = string.CompareOrdinal(xSource.Filename, ySource.Filename);
            }
            return result;
        }

        public bool Equals(IVersionableConformanceResource x, IVersionableConformanceResource y)
        {
            var xSource = (x as Resource).Annotation<ResourcePackageSource>();
            var ySource = (y as Resource).Annotation<ResourcePackageSource>();
            if (xSource != null && ySource != null)
            {
                return xSource.PackageId == ySource.PackageId
                    && xSource.PackageVersion == ySource.PackageVersion
                    && xSource.Filename == ySource.Filename;
            }
            return false;
        }

        public int GetHashCode([DisallowNull] IVersionableConformanceResource obj)
        {
            if (obj != null)
            {
                string result = $"{obj.Url}|{obj.Version}";
                var source = (obj as Resource).Annotation<ResourcePackageSource>();
                if (source != null)
                    result = $"result from {source.PackageId}|{source.PackageVersion} - {source.Filename}";
                return result.GetHashCode();

            }
            return obj.GetHashCode();
        }
    }
}
