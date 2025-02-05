using Hl7.Fhir.Model;

namespace UploadFIG
{
	[System.Diagnostics.DebuggerDisplay(@"Canonical: {canonical}|{version}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
	public class CanonicalDetails
    {
        public CanonicalDetails() { }
		public string resourceType { get; set; }
        public string canonical { get; set; }
        public string version { get; set; }
        public string status { get; set; }
        public string name { get; set; }

        public Resource resource { get; set; }

        public List<Resource> requiredBy { get; } = new List<Resource>();
    }
}
