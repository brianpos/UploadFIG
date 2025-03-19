using System.Diagnostics.CodeAnalysis;

namespace UploadFIG
{
    public class CanonicalDetailsComparer : IEqualityComparer<CanonicalDetails>
    {
        public bool Equals(CanonicalDetails x, CanonicalDetails y)
        {
            return (x as IComparable<CanonicalDetails>).CompareTo(y) == 0;
        }

        public int GetHashCode([DisallowNull] CanonicalDetails obj)
        {
            return $"{obj.Canonical}|{obj.Version}".GetHashCode();
        }
    }
}
