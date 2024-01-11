using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UploadFIG.PackageHelpers
{
	[System.Diagnostics.DebuggerDisplay(@"Url: {url}, Package: {packageId}|{packageVersion}")] // http://blogs.msdn.com/b/jaredpar/archive/2011/03/18/debuggerdisplay-attribute-best-practices.aspx
	public class PackageCacheItem
	{
		public string packageId { get; set; }

		public string packageVersion { get; set; }

		public string filename { get; set; }

		public string resourceType { get; set; }

		public string id { get; set; }

		public string url { get; set; }

		public string version { get; set; }

		public string type { get; set; }

		public List<PackageCacheItem> duplicates { get; set; } = new List<PackageCacheItem>();
	}
}
