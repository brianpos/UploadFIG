using Firely.Fhir.Packages;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace UploadFIG
{
    public class TempPackageCache
    {
		public void RegisterPackage(string packageId, string version, Stream stream)
		{
			_memCache.Add($"{packageId}|{version}", stream);
		}
		private Dictionary<string, Stream> _memCache = new Dictionary<string, Stream>();

		public TempPackageCache()
        {
            // get the name of a new folder in the User's local temp directory
            _cacheFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UploadFIG", "PackageCache");
            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
            else
            {
				// TODO: Check if there is any "old" content in here and delete that
				DirectoryInfo di = new DirectoryInfo(_cacheFolder);
				var directorySize = di.GetFiles().Sum(fsi => fsi.Length);
				Console.WriteLine($"Package cache folder size: {directorySize/1024/1024} MB");
			}
        }

        string _cacheFolder;

        /// <summary>
        /// Return an opened stream for this specific version of the package (downloading if not present)
        /// </summary>
        /// <param name="packageId"></param>
        /// <param name="version"></param>
        /// <returns>A stream or null if the package did not exist</returns>
        public Stream GetPackageStream(string packageId, string version)
        {
			// Check the in mem cache first - note this is only intended to hold the primary package being processed
			// not the others as they go
			Stream result;
			if (_memCache.TryGetValue($"{packageId}|{version}", out result))
				return result;

			if (version.StartsWith("current") || version == "dev")
            {
                // Bail for non registry CI content
                return null;
            }

			if (TryGetPackageStreamFromRegistry(packageId, version, out result))
				return result;

			if (TryGetPackageStreamFromRegistry(packageId, version, out var streamPackages2Registry, "https://packages2.fhir.org/packages"))
				return streamPackages2Registry;

			// Lets check with the package indexes for all the available versions
			if (SemanticVersioning.Range.TryParse(version, true, out var range))
			{
				PackageClient pc = PackageClient.Create();
				var examplesPkg = pc.GetPackage(new PackageReference(packageId, null)).WaitResult();
				string contents = Encoding.UTF8.GetString(examplesPkg);
				var pl = JsonConvert.DeserializeObject<PackageListing>(contents);
				Console.WriteLine($"Package ID: {pl?.Name}");
				Console.WriteLine($"Package Title: {pl?.Description}");
				Console.WriteLine($"All Versions: {String.Join(", ", pl.Versions.Keys)}");

				// Recheck the range order here (highly likely upside down now)
				var validVersions = pl.Versions.Keys.Select(v => SemanticVersioning.Version.Parse(v, true)).Where(sv => range.IsSatisfied(sv)).OrderDescending();
				Console.WriteLine($"Valid Versions: {String.Join(", ", validVersions.Select(v => v.ToString()))}");

				if (validVersions.Any())
				{
					version = validVersions.First().ToString();
					if (TryGetPackageStreamFromRegistry(packageId, version, out result))
						return result;
					if (TryGetPackageStreamFromRegistry(packageId, version, out result, "https://packages2.fhir.org/packages"))
						return result;
				}
			}

			return null;
        }

		private bool TryGetPackageStreamFromRegistry(string packageId, string version, out Stream result, string registry = null)
		{
			try
			{
				// download if the package is not already in the cache
				string packageFile = System.IO.Path.Combine(_cacheFolder, packageId + "_" + version.Replace(".", "_") + ".tgz");
				if (!System.IO.File.Exists(packageFile))
				{
					// Now download the package from the registry into the cache
					PackageClient pc;
					if (string.IsNullOrEmpty(registry))
						pc = PackageClient.Create();
					else
						pc = PackageClient.Create(registry); //  "https://packages2.fhir.org/packages");
					var rawPackage = pc.GetPackage(new PackageReference(packageId, version)).WaitResult();
					System.IO.File.WriteAllBytes(packageFile, rawPackage);
					result = new MemoryStream(rawPackage);
					return true;
				}

				// Package is already here, just return a stream on it
				result =  File.OpenRead(packageFile);
				return true;
			}
			catch (Exception ex)
			{
				result = null;
				return false;
			}
		}
	}
}
