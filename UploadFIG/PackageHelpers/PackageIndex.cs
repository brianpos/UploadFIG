using System.Text.Json.Serialization;

namespace UploadFIG
{
    /// <summary>
    /// This class is a direct representation of the `.index.json` file inlcuded in the Package directory of a FHIR TGZ Package.
    /// </summary>
    public class PackageIndex
    {
        [JsonPropertyName("index-version")]
        public int indexVersion { get; set; }

        [JsonPropertyName("files")]
        public List<FileDetail> Files { get; set; } = new List<FileDetail>();
    }

    public class FileDetail
    {
        public string filename { get; set; }

        public string resourceType { get; set; }

        public string id { get; set; }

        public string url { get; set; }

        public string version { get; set; }

        public string type { get; set; }
    }
}
