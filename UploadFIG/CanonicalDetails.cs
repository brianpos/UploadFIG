using Hl7.Fhir.Model;

namespace UploadFIG
{
	[System.Diagnostics.DebuggerDisplay(@"Canonical: {canonical}|{version}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
	public class CanonicalDetails : IComparable<CanonicalDetails>
	{
        public CanonicalDetails() { }
		public string resourceType { get; set; }
        public string canonical { get; set; }
        public string version { get; set; }
        public string status { get; set; }
        public string name { get; set; }

        public Resource resource { get; set; }

        public List<Resource> requiredBy { get; } = new List<Resource>();

		int IComparable<CanonicalDetails>.CompareTo(CanonicalDetails other)
		{
			if (other == null)
				return -1;
			int result = string.CompareOrdinal(canonical, other.canonical);
			if (result == 0)
				result = string.CompareOrdinal(version, other.version);
			return result;
		}
	}
}
