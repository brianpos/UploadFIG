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

    public class OutputDependenciesFile
    {
        /// <summary>
        /// Package ID that was processed
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Version of the Package that was processed
        /// </summary>
        public string version { get; set; }

        /// <summary>
        /// Date this package was processed
        /// </summary>
        public string date { get; set; }

        /// <summary>
        /// Source Location that the package was downloaded from (or local folder selected)
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// Title defined in the package manifest processed
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// The Version of FHIR of the content in the package
        /// </summary>
        public string fhirVersion { get; set; }

        /// <summary>
        /// Set of package dependencies defined in the package
        /// </summary>
        public Dictionary<string, string> dependencies { get; set; } = new Dictionary<string, string>();

        public List<DependentResource> externalCanonicalsRequired { get; set; } = new List<DependentResource>();

        public List<CanonicalDetails> containedCanonicals{ get; set; } = new();
    }

    public class DependentResource
    {
        public string resourceType { get; set; }
        public string canonical { get; set; }
        public string version { get; set; }
        public string status { get; set; }
        public string foundInPackage { get; set; }
        public bool? isMissing { get; set; }
    }
}
