using System.Text.Json.Serialization;
using Hl7.Fhir.Model;

namespace UploadFIG
{
    [System.Diagnostics.DebuggerDisplay(@"Canonical: {canonical}|{version}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
    public class CanonicalDetails : IComparable<CanonicalDetails>
    {
        public CanonicalDetails() { }

        [JsonPropertyName("resourceType")]
        public string ResourceType { get; set; }

        [JsonPropertyName("canonical")]
        public string Canonical { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        public Resource resource { get; set; }

        public List<Resource> requiredBy { get; } = new List<Resource>();

        int IComparable<CanonicalDetails>.CompareTo(CanonicalDetails other)
        {
            if (other == null)
                return -1;
            int result = string.CompareOrdinal(Canonical, other.Canonical);
            if (result == 0)
                result = string.CompareOrdinal(Version, other.Version);
            return result;
        }
    }
}
