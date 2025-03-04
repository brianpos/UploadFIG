using System.Text.Json.Serialization;

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
	}
}
