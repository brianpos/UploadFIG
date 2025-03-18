using System.Text.Json.Serialization;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace UploadFIG
{
	/// <summary>
	/// This class is a direct representation of the `.index.json` file included in the Package directory of a FHIR TGZ Package.
	/// </summary>
	public class PackageIndex
	{
		[JsonPropertyName("index-version")]
		public int indexVersion { get; set; }

		[JsonPropertyName("files")]
		public List<FileDetail> Files { get; set; } = new List<FileDetail>();
	}

	[System.Diagnostics.DebuggerDisplay(@"{resourceType}/{id}   {url}|{version}   filename: {filename}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
	public class FileDetail
	{
		public string filename { get; set; }

		public string resourceType { get; set; }

		public string id { get; set; }

		public string url { get; set; }

		public string version { get; set; }

		public string type { get; set; }

		[JsonIgnore]
		public bool detectedInvalidContent { get; set; } = false;

		[JsonIgnore]
		public Hl7.Fhir.Model.Resource resource { get; set; }

		[JsonIgnore]
		public bool hasDuplicateDefinitions { get; set; } = false;

        [JsonIgnore]
        public bool? ScannedForDependencies;

        /// <summary>
        /// Still need to work out what this is indexing here, but an empty value means it's not used and can be ignored
        /// </summary>
        [JsonIgnore]
        public List<string> UsedBy { get; set; } = new ();

        public void MarkUsedBy(CanonicalDetails details)
        {
            MarkUsedBy(details.requiredBy.First());
        }

        public void MarkUsedBy(Resource resource)
        {
            UsedBy.Add($"{resource.TypeName}/{resource.Id}");
        }
    }
}
